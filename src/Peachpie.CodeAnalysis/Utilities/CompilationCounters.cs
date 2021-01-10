using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
using Pchp.CodeAnalysis.Symbols;

namespace Peachpie.CodeAnalysis.Utilities
{
    internal class CompilationCounters
    {
        private readonly PhpCompilation _compilation;
        private CallSiteCounter _callSiteCounter;

        public CompilationCounters(PhpCompilation compilation)
        {
            _compilation = compilation;
        }

        /// <summary>
        /// Obtain the values passed to the constructor of <see cref="CoreTypes.CompilationCountersAttribute"/>.
        /// </summary>
        public IEnumerable<int> GetAttributeCtorArgs()
        {
            // Total number of routines
            yield return _compilation.SourceSymbolCollection.AllRoutines.Count();

            // Number of global functions
            yield return _compilation.SourceSymbolCollection.GetFunctions().Count();

            // Number of specializations
            yield return
                _compilation.SourceSymbolCollection.AllRoutines
                    .SelectMany(r => r.SpecializedOverloads)
                    .Count();

            // Call site metrics
            _callSiteCounter ??= CallSiteCounter.CountCallSites(_compilation);
            yield return _callSiteCounter.RoutineCalls;
            yield return _callSiteCounter.FunctionCalls;
            yield return _callSiteCounter.LibraryFunctionCalls;
            yield return _callSiteCounter.AmbiguousSourceFunctionCalls;
            yield return _callSiteCounter.BranchedSourceFunctionCalls;
            yield return _callSiteCounter.OriginalSourceFunctionCalls;
            yield return _callSiteCounter.SpecializedSourceFunctionCalls;
        }

        private class CallSiteCounter : GraphExplorer<VoidStruct>
        {
            public int RoutineCalls { get; private set; }
            public int FunctionCalls { get; private set; }
            public int LibraryFunctionCalls { get; private set; }
            public int AmbiguousSourceFunctionCalls { get; private set; }
            public int BranchedSourceFunctionCalls { get; private set; }
            public int OriginalSourceFunctionCalls { get; private set; }
            public int SpecializedSourceFunctionCalls { get; private set; }

            private CallSiteCounter() {}

            public static CallSiteCounter CountCallSites(PhpCompilation compilation)
            {
                var counter = new CallSiteCounter();
                var baseRoutines = compilation.SourceSymbolCollection.AllRoutines.ToArray();
                var routines = baseRoutines.Concat(baseRoutines.SelectMany(r => r.SpecializedOverloads));

                foreach (var routine in routines)
                {
                    if (routine.ControlFlowGraph != null)
                    {
                        counter.VisitCFG(routine.ControlFlowGraph);
                    }
                }

                return counter;
            }

            protected override VoidStruct VisitRoutineCall(BoundRoutineCall x)
            {
                RoutineCalls++;

                if (x is BoundGlobalFunctionCall)
                {
                    FunctionCalls++;
                }

                switch (x.TargetMethod)
                {
                    case PEMethodSymbol _ when x is BoundGlobalFunctionCall:
                        LibraryFunctionCalls++;
                        break;

                    case SourceFunctionSymbol { IsSpecializedOverload: true }:
                        SpecializedSourceFunctionCalls++;
                        break;

                    case SourceFunctionSymbol { SpecializedOverloads: { IsEmpty: false } }:
                        OriginalSourceFunctionCalls++;
                        break;

                    case AmbiguousMethodSymbol ambig:
                        if (OptimizationUtils.TryExtractOriginalFromSpecializedOverloads(ambig.Ambiguities, out var orig) && orig is SourceFunctionSymbol)
                        {
                            BranchedSourceFunctionCalls++;
                        }
                        else if (ambig.Ambiguities[0] is PEMethodSymbol m && x is BoundGlobalFunctionCall)
                        {
                            LibraryFunctionCalls++;
                        }
                        else if (ambig.Ambiguities[0] is SourceFunctionSymbol)
                        {
                            AmbiguousSourceFunctionCalls++;
                        }
                        break;
                }

                return base.VisitRoutineCall(x);
            }
        }
    }
}
