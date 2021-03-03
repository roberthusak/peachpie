﻿using Devsense.PHP.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Pchp.CodeAnalysis.CodeGen;
using Pchp.CodeAnalysis.CommandLine;
using Pchp.CodeAnalysis.Emit;
using Pchp.CodeAnalysis.FlowAnalysis;
using Pchp.CodeAnalysis.FlowAnalysis.Passes;
using Pchp.CodeAnalysis.FlowAnalysis.Souffle;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
using Pchp.CodeAnalysis.Semantics.Model;
using Pchp.CodeAnalysis.Symbols;
using Pchp.CodeAnalysis.Utilities;
using Peachpie.CodeAnalysis.Utilities;
using Roslyn.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Devsense.PHP.Syntax.Ast;
using Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization;

namespace Pchp.CodeAnalysis
{
    /// <summary>
    /// Performs compilation of all source methods.
    /// </summary>
    internal class SourceCompiler
    {
        readonly PhpCompilation _compilation;
        readonly PEModuleBuilder _moduleBuilder;
        readonly bool _emittingPdb;
        readonly DiagnosticBag _diagnostics;
        readonly CancellationToken _cancellationToken;

        readonly Worklist<BoundBlock> _worklist;

        /// <summary>
        /// Number of control flow graph transformation cycles to do at most.
        /// <c>0</c> to disable the lowering.
        /// </summary>
        int MaxTransformCount => _compilation.Options.OptimizationLevel.GraphTransformationCount();

        public bool ConcurrentBuild => _compilation.Options.ConcurrentBuild;

        private SourceCompiler(PhpCompilation compilation, PEModuleBuilder moduleBuilder, bool emittingPdb, DiagnosticBag diagnostics, CancellationToken cancellationToken)
        {
            Contract.ThrowIfNull(compilation);
            Contract.ThrowIfNull(diagnostics);

            _compilation = compilation;
            _moduleBuilder = moduleBuilder;
            _emittingPdb = emittingPdb;
            _diagnostics = diagnostics;
            _cancellationToken = cancellationToken;

            // parallel worklist algorithm
            _worklist = new Worklist<BoundBlock>(AnalyzeBlock);

            // semantic model
        }

        void WalkMethods(Action<SourceRoutineSymbol> action, bool allowParallel = false)
        {
            var baseRoutines = _compilation.SourceSymbolCollection.AllRoutines;
            var routines = baseRoutines.Concat(baseRoutines.SelectMany(r => r.SpecializedOverloads));

            if (ConcurrentBuild && allowParallel)
            {
                Parallel.ForEach(routines, action);
            }
            else
            {
                routines.ForEach(action);
            }
        }

        void WalkSourceFiles(Action<SourceFileSymbol> action, bool allowParallel = false)
        {
            var files = _compilation.SourceSymbolCollection.GetFiles();

            if (ConcurrentBuild && allowParallel)
            {
                Parallel.ForEach(files, action);
            }
            else
            {
                files.ForEach(action);
            }
        }

        void WalkTypes(Action<SourceTypeSymbol> action, bool allowParallel = false)
        {
            var types = _compilation.SourceSymbolCollection.GetTypes();

            if (ConcurrentBuild && allowParallel)
            {
                Parallel.ForEach(types, action);
            }
            else
            {
                types.ForEach(action);
            }
        }

        /// <summary>
        /// Enqueues routine's start block for analysis.
        /// </summary>
        void EnqueueRoutine(SourceRoutineSymbol routine)
        {
            Contract.ThrowIfNull(routine);

            // lazily binds CFG and
            // adds their entry block to the worklist

            // TODO: reset LocalsTable, FlowContext and CFG

            _worklist.Enqueue(routine.ControlFlowGraph?.Start);

            // enqueue routine parameter default values
            foreach (var p in routine.SourceParameters)
            {
                if (p.Initializer != null)
                {
                    EnqueueExpression(p.Initializer, routine.TypeRefContext);
                }

                EnqueueAttributes(p.SourceAttributes);
            }

            EnqueueAttributes(routine.SourceAttributes);
        }

        void EnqueueAttributes(IEnumerable<SourceCustomAttribute> attributes)
        {
            foreach (var attr in attributes)
            {
                foreach (var a in attr.Arguments)
                {
                    EnqueueExpression(a.Value, attr.TypeCtx);
                }
            }
        }

        /// <summary>
        /// Enqueues the standalone expression for analysis.
        /// </summary>
        void EnqueueExpression(BoundExpression expression, TypeRefContext/*!*/ctx)
        {
            Contract.ThrowIfNull(expression);
            Contract.ThrowIfNull(ctx);

            var dummy = new BoundBlock()
            {
                FlowState = new FlowState(new FlowContext(ctx, null)),
            };

            dummy.Add(new BoundExpressionStatement(expression));

            _worklist.Enqueue(dummy);
        }

        /// <summary>
        /// Enqueues initializers of a class fields and constants, and type custom attributes.
        /// </summary>
        void EnqueueType(SourceTypeSymbol type)
        {
            type.GetDeclaredMembers().OfType<SourceFieldSymbol>().ForEach(f =>
            {
                if (f.Initializer != null)
                {
                    EnqueueExpression(
                        f.Initializer,
                        f.EnsureTypeRefContext());
                }

                EnqueueAttributes(f.SourceAttributes);
            });

            EnqueueAttributes(type.SourceAttributes.OfType<SourceCustomAttribute>());
        }

        internal void ReanalyzeMethods()
        {
            this.WalkMethods(routine => _worklist.Enqueue(routine.ControlFlowGraph.Start));
        }

        internal void AnalyzeMethods()
        {
            // _worklist.AddAnalysis:

            // TypeAnalysis + ResolveSymbols
            // LowerBody(block)

            // analyse blocks
            _worklist.DoAll(concurrent: ConcurrentBuild);
        }

        void AnalyzeBlock(BoundBlock block) // TODO: driver
        {
            // TODO: pool of CFGAnalysis
            // TODO: async
            // TODO: in parallel

            block.Accept(AnalysisFactory(block.FlowState));
        }

        GraphVisitor<VoidStruct> AnalysisFactory(FlowState state)
        {
            return new ExpressionAnalysis<VoidStruct>(_worklist, _compilation.GlobalSemantics);
        }

        /// <summary>
        /// Walks all expressions and resolves their access, operator method, and result CLR type.
        /// </summary>
        void BindTypes(IEnumerable<SourceRoutineSymbol> specificRoutines = null)
        {
            if (specificRoutines == null)
            {
                // method bodies
                this.WalkMethods(BindRoutine, allowParallel: ConcurrentBuild);

                // field initializers
                WalkTypes(type =>
                {
                    type.GetDeclaredMembers().OfType<SourceFieldSymbol>().ForEach(
                        field => new ResultTypeBinder(_compilation).Bind(field));

                }, allowParallel: ConcurrentBuild);
            }
            else
            {
                specificRoutines.ForEach(BindRoutine);
            }

            void BindRoutine(SourceRoutineSymbol routine)
            {
                var binder = new ResultTypeBinder(_compilation);

                // body
                binder.Bind(routine);

                // parameter initializers
                routine.SourceParameters.ForEach(binder.Bind);
            }
        }

        private ImmutableArray<SourceRoutineSymbol> SpecializeMethods()
        {
            var overloads = new ConcurrentBag<SourceRoutineSymbol>();
            this.WalkMethods(routine =>
            {
                // TODO: Enable to add more specializations to a method (currently it sticks only with the first one)
                if (routine.SpecializedOverloads.IsEmpty && !routine.IsSpecializedOverload)
                {
                    if (_compilation.RoutineSpecializer.TryGetRoutineSpecializedParameters(routine, out var specParams))
                    {
                        var overload = new SourceFunctionSymbol(routine.ContainingFile, (FunctionDecl) routine.Syntax)
                        {
                            SpecializedParameterTypes = specParams.Set.First()
                        };

                        routine.SpecializedOverloads = ImmutableArray.Create((SourceRoutineSymbol) overload);

                        // Bind the overload and prepare for the next analysis
                        _worklist.Enqueue(overload.ControlFlowGraph.Start);

                        overloads.Add(overload);
                    }
                }
            }, allowParallel: ConcurrentBuild);

            return overloads.ToImmutableArray();
        }

        #region Nested class: LateStaticCallsLookup

        /// <summary>
        /// Lookups self:: and parent:: static method calls.
        /// </summary>
        class LateStaticCallsLookup : GraphExplorer<bool>
        {
            List<MethodSymbol> _lazyStaticCalls;

            public static IList<MethodSymbol> GetSelfStaticCalls(BoundBlock block)
            {
                var visitor = new LateStaticCallsLookup();
                visitor.Accept(block);
                return (IList<MethodSymbol>)visitor._lazyStaticCalls ?? Array.Empty<MethodSymbol>();
            }

            public override bool VisitStaticFunctionCall(BoundStaticFunctionCall x)
            {
                if (x.TypeRef.IsSelf() || x.TypeRef.IsParent())
                {
                    if (_lazyStaticCalls == null) _lazyStaticCalls = new List<MethodSymbol>();

                    if (x.TargetMethod.IsValidMethod() && x.TargetMethod.IsStatic)
                    {
                        _lazyStaticCalls.Add(x.TargetMethod);
                    }
                    else if (x.TargetMethod is AmbiguousMethodSymbol ambiguous)
                    {
                        _lazyStaticCalls.AddRange(ambiguous.Ambiguities.Where(sm => sm.IsStatic));
                    }
                }

                return base.VisitStaticFunctionCall(x);
            }
        }

        #endregion

        /// <summary>
        /// Walks static methods that don't use late static binding and checks if it should forward the late static type;
        /// hence it must know the late static as well.
        /// </summary>
        void ForwardLateStaticBindings()
        {
            var calls = new ConcurrentBag<KeyValuePair<SourceMethodSymbol, MethodSymbol>>();

            // collect self:: or parent:: static calls
            this.WalkMethods(routine =>
            {
                if (routine is SourceMethodSymbol caller && caller.IsStatic && (caller.Flags & RoutineFlags.UsesLateStatic) == 0)
                {
                    var cfg = caller.ControlFlowGraph;
                    if (cfg == null)
                    {
                        return;
                    }

                    // has self:: or parent:: call to a routine?
                    var selfcalls = LateStaticCallsLookup.GetSelfStaticCalls(cfg.Start);
                    if (selfcalls.Count == 0)
                    {
                        return;
                    }

                    // routine requires late static type?
                    if (selfcalls.Any(sm => sm.HasLateStaticBoundParam()))
                    {
                        caller.Flags |= RoutineFlags.UsesLateStatic;
                    }
                    else
                    {
                        foreach (var callee in selfcalls)
                        {
                            calls.Add(new KeyValuePair<SourceMethodSymbol, MethodSymbol>(caller, callee));
                        }
                    }
                }
            }, allowParallel: ConcurrentBuild);

            // process edges between caller and calles until we forward all the late static calls
            int forwarded;
            do
            {
                forwarded = 0;
                Parallel.ForEach(calls, edge =>
                {
                    if ((edge.Key.Flags & RoutineFlags.UsesLateStatic) == 0 && edge.Value.HasLateStaticBoundParam())
                    {
                        edge.Key.Flags |= RoutineFlags.UsesLateStatic;
                        Interlocked.Increment(ref forwarded);
                    }
                });

            } while (forwarded != 0);
        }

        internal void DiagnoseMethods()
        {
            this.WalkMethods(DiagnoseRoutine, allowParallel: true);
        }

        private void DiagnoseRoutine(SourceRoutineSymbol routine)
        {
            Contract.ThrowIfNull(routine);

            DiagnosticWalker<VoidStruct>.Analyse(_diagnostics, routine);
        }

        internal void DiagnoseFiles()
        {
            var files = _compilation.SourceSymbolCollection.GetFiles();

            if (ConcurrentBuild)
            {
                Parallel.ForEach(files, DiagnoseFile);
            }
            else
            {
                files.ForEach(DiagnoseFile);
            }
        }

        private void DiagnoseFile(SourceFileSymbol file)
        {
            file.GetDiagnostics(_diagnostics);
        }

        private void DiagnoseTypes()
        {
            this.WalkTypes(DiagnoseType, allowParallel: true);
        }

        private void DiagnoseType(SourceTypeSymbol type)
        {
            type.GetDiagnostics(_diagnostics);
        }

        bool TransformMethods(bool allowParallel)
        {
            bool anyTransforms = false;
            var delayedTrn = new DelayedTransformations();

            this.WalkMethods(m =>
                {
                    // Cannot be simplified due to multithreading ('=' is atomic unlike '|=')
                    if (TransformationRewriter.TryTransform(delayedTrn, m))
                        anyTransforms = true;
                },
                allowParallel: allowParallel);

            // Apply transformation that cannot run in the parallel way
            anyTransforms |= delayedTrn.Apply();

            return anyTransforms;
        }

        internal void EmitMethodBodies()
        {
            Debug.Assert(_moduleBuilder != null);

            // source routines
            this.WalkMethods(this.EmitMethodBody, allowParallel: true);
        }

        internal void EmitSynthesized()
        {
            // TODO: Visit every symbol with Synthesize() method and call it instead of followin

            // ghost stubs, overrides
            WalkMethods(f => f.SynthesizeStubs(_moduleBuilder, _diagnostics));
            WalkTypes(t => t.FinalizeMethodTable(_moduleBuilder, _diagnostics));

            // bootstrap, __statics.Init, .ctor
            WalkTypes(t => t.SynthesizeInit(_moduleBuilder, _diagnostics));
            WalkSourceFiles(f => f.SynthesizeInit(_moduleBuilder, _diagnostics));

            // realize .cctor if any
            _moduleBuilder.RealizeStaticCtors();
        }

        /// <summary>
        /// Generates analyzed method.
        /// </summary>
        void EmitMethodBody(SourceRoutineSymbol routine)
        {
            Contract.ThrowIfNull(routine);

            if (routine.ControlFlowGraph != null)   // non-abstract method
            {
                Debug.Assert(routine.ControlFlowGraph.Start.FlowState != null);

                var body = MethodGenerator.GenerateMethodBody(_moduleBuilder, routine, 0, null, _diagnostics, _emittingPdb);
                _moduleBuilder.SetMethodBody(routine, body);
            }
        }

        void CompileEntryPoint()
        {
            if (_compilation.Options.OutputKind.IsApplication() && _moduleBuilder != null)
            {
                var entryPoint = _compilation.GetEntryPoint(_cancellationToken);
                if (entryPoint != null && !(entryPoint is ErrorMethodSymbol))
                {
                    // wrap call to entryPoint within real <Script>.EntryPointSymbol
                    _moduleBuilder.CreateEntryPoint((MethodSymbol)entryPoint, _diagnostics);

                    //
                    Debug.Assert(_moduleBuilder.ScriptType.EntryPointSymbol != null);
                    _moduleBuilder.SetPEEntryPoint(_moduleBuilder.ScriptType.EntryPointSymbol, _diagnostics);
                }
            }
        }

        bool RewriteMethods(bool forceAnalysisInvalidation)
        {
            using (_compilation.StartMetric("transform"))
            {
                if (TransformMethods(ConcurrentBuild) || forceAnalysisInvalidation)
                {
                    WalkMethods(m =>
                        {
                            m.ControlFlowGraph?.FlowContext?.InvalidateAnalysis();
                            EnqueueRoutine(m);
                        },
                        allowParallel: true);
                }
                else
                {
                    // No changes performed => no need to repeat the analysis
                    return false;
                }
            }

            //
            return true;
        }

        public static IEnumerable<Diagnostic> BindAndAnalyze(PhpCompilation compilation, CancellationToken cancellationToken)
        {
            var manager = compilation.GetBoundReferenceManager();   // ensure the references are resolved! (binds ReferenceManager)

            var diagnostics = new DiagnosticBag();
            var compiler = new SourceCompiler(compilation, null, true, diagnostics, cancellationToken);

            using (compilation.StartMetric("bind"))
            {
                // 1. Bind Syntax & Symbols to Operations (CFG)
                //   a. construct CFG, bind AST to Operation
                //   b. declare table of local variables
                compiler.WalkMethods(compiler.EnqueueRoutine, allowParallel: true);
                compiler.WalkTypes(compiler.EnqueueType, allowParallel: true);
            }

            if (compilation.Options.ExportSouffleRelations)
            {
                // 1c. If requested, export the CFGs into datalog relations for Souffle
                string souffleDir = Path.Combine(compilation.Options.BaseDirectory, "souffle", "input");
                Directory.CreateDirectory(souffleDir);
                SouffleEncoder.Encode(compilation, souffleDir);
            }

            // Repeat analysis and transformation until either the limit is met or there are no more changes
            int transformation = 0;
            var specializedRoutines = ImmutableArray<SourceRoutineSymbol>.Empty;
            do
            {
                using (compilation.StartMetric("analysis"))
                {
                    // 2. Analyze Operations
                    //   a. type analysis (converge type - mask), resolve symbols
                    //   b. lower semantics, update bound tree, repeat
                    compiler.AnalyzeMethods();
                }

                using (compilation.StartMetric("bind types"))
                {
                    // 3. Resolve operators and types
                    compiler.BindTypes();
                }

                //TypingCounter.CountTypingInfo(compilation);

                // 4. Optionally create and analyse routine specializations
                if (compilation.RoutineSpecializer != null)
                {
                    using (compilation.StartMetric("specialization"))
                    {
                        compilation.RoutineSpecializer.OnAfterAnalysis(compiler._worklist.CallGraph);
                        specializedRoutines = compiler.SpecializeMethods();
                    }

                    using (compilation.StartMetric("specialization analysis"))
                    {
                        // Analyze only the newly specialized methods (they were put to the worklist)
                        compiler.AnalyzeMethods();
                    }

                    using (compilation.StartMetric("specialization type binding"))
                    {
                        // Bind only the recently specialized routines
                        compiler.BindTypes(specializedRoutines);
                    }
                }

                using (compilation.StartMetric(nameof(ForwardLateStaticBindings)))
                {
                    // 5. forward the late static type if needed
                    compiler.ForwardLateStaticBindings();
                }

                // 6. Transform Semantic Trees for Runtime Optimization
            } while (
                transformation++ < compiler.MaxTransformCount               // limit number of lowering cycles
                && !cancellationToken.IsCancellationRequested               // user canceled ?
                && compiler.RewriteMethods(!specializedRoutines.IsEmpty));  // try lower the semantics (repeat also when specialized overloads were produced)

            // Track the number of actually performed transformations
            compilation.TrackMetric("transformations", transformation - 1);

            using (compilation.StartMetric("diagnostic"))
            {
                // 7. Collect diagnostics
                compiler.DiagnoseMethods();
                compiler.DiagnoseTypes();
                compiler.DiagnoseFiles();
            }

            //
            return diagnostics.AsEnumerable();
        }

        public static void CompileSources(
            PhpCompilation compilation,
            PEModuleBuilder moduleBuilder,
            bool emittingPdb,
            bool hasDeclarationErrors,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            Debug.Assert(moduleBuilder != null);

            compilation.TrackMetric("sourceFilesCount", compilation.SourceSymbolCollection.FilesCount);

            using (compilation.StartMetric("diagnostics"))
            {
                // ensure flow analysis and collect diagnostics
                var declarationDiagnostics = compilation.GetDeclarationDiagnostics(cancellationToken);
                diagnostics.AddRange(declarationDiagnostics);

                // cancel the operation if there are errors
                if (hasDeclarationErrors |= declarationDiagnostics.HasAnyErrors() || cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }

            //
            var compiler = new SourceCompiler(compilation, moduleBuilder, emittingPdb, diagnostics, cancellationToken);

            using (compilation.StartMetric("emit"))
            {
                // Emit method bodies
                //   a. declared routines
                //   b. synthesized symbols
                compiler.EmitMethodBodies();
                compiler.EmitSynthesized();

                // Entry Point (.exe)
                compiler.CompileEntryPoint();
            }
        }
    }
}
