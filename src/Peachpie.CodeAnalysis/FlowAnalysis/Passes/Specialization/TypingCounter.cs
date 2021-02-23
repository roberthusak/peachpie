using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Devsense.PHP.Syntax.Ast;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.Utilities;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    internal class TypingCounter : GraphExplorer<VoidStruct>
    {
        private readonly PhpCompilation _compilation;

        private SourceRoutineSymbol _routine;

        private readonly HashSet<ParameterSymbol> _routineCheckedParameters = new HashSet<ParameterSymbol>();

        public int TotalParameters { get; private set; }
        public int TypeCheckedParameters { get; private set; }
        public int TwiceTypeCheckedParameters { get; private set; }
        public int PassedFurtherParameters { get; private set; }
        public int ArrayAccessedParameters { get; private set; }
        public int FieldAccessedParameters { get; private set; }
        public int MethodInvokedParameters { get; private set; }
        public int UniqueMethodInvokedParameters { get; private set; }
        public int ConcatenatedParameters { get; private set; }
        public int EqualityComparedParameters { get; private set; }
        public int ConvertedParameters { get; private set; }
        public int IssetParameters { get; private set; }

        public Dictionary<MethodSymbol, int> TypeCheckCounts { get; } = new Dictionary<MethodSymbol, int>();

        public Dictionary<TypeSymbol, int> InstanceOfCounts { get; } = new Dictionary<TypeSymbol, int>();

        private TypingCounter(PhpCompilation compilation)
        {
            _compilation = compilation;
        }

        public static TypingCounter CountTypingInfo(PhpCompilation compilation)
        {
            int totalRoutines = 0;
            int routinesWithTypeChecks = 0;

            var counter = new TypingCounter(compilation);
            var routines =
                compilation.SourceSymbolCollection.GetFunctions();
            //.Concat(
            //    compilation.SourceSymbolCollection.GetTypes()
            //        .SelectMany(f => f.GetMembers().OfType<SourceRoutineSymbol>()));
            //compilation.SourceSymbolCollection.GetTypes()
            //    .SelectMany(f => f.GetMembers().OfType<SourceRoutineSymbol>())
            foreach (var function in routines)
            {
                if (function.ControlFlowGraph != null)
                {
                    counter.TotalParameters += function.SourceParameters.Length;

                    counter._routine = function;
                    counter.VisitCFG(function.ControlFlowGraph);

                    counter.TypeCheckedParameters += counter._routineCheckedParameters.Count;

                    totalRoutines++;
                    if (counter._routineCheckedParameters.Count > 0)
                    {
                        routinesWithTypeChecks++;
                    }

                    counter._routineCheckedParameters.Clear();
                }
            }

            counter._routine = null;
            return counter;
        }

        public override VoidStruct VisitGlobalFunctionCall(BoundGlobalFunctionCall x)
        {
            if (x.Name.IsDirect && x.TargetMethod != null)
            {
                if (x.ArgumentsInSourceOrder.Length == 1
                    && x.ArgumentsInSourceOrder[0].Value is BoundVariableRef { Variable: ParameterReference paramRef })
                {
                    switch (x.Name.NameValue.Name.Value)
                    {
                        case "is_int":
                        case "is_integer":
                        case "is_long":
                        case "is_bool":
                        case "is_float":
                        case "is_double":
                        case "is_real":
                        case "is_string":
                        case "is_resource":
                        case "is_null":
                        case "empty":       // TRUE if null
                        case "is_array":
                        case "is_object":
                        case "is_numeric":
                        case "is_callable":
                            NoteCheckedParameter(paramRef.Parameter);
                            TypeCheckCounts[x.TargetMethod] =
                                TypeCheckCounts.TryGetValue(x.TargetMethod, out int count)
                                    ? count + 1
                                    : 1;
                            break;
                    }
                }

                if (x.TargetMethod is SourceFunctionSymbol)
                {
                    foreach (var arg in x.ArgumentsInSourceOrder)
                    {
                        if (arg.Value is BoundVariableRef { Variable: ParameterReference _ })
                        {
                            PassedFurtherParameters++;
                        }
                    }
                }
            }

            return base.VisitGlobalFunctionCall(x);
        }

        public override VoidStruct VisitInstanceOf(BoundInstanceOfEx x)
        {
            if (x.AsType.Type != null && x.Operand is BoundVariableRef { Variable: ParameterReference paramRef })
            {
                NoteCheckedParameter(paramRef.Parameter);
                InstanceOfCounts[(TypeSymbol)x.AsType.Type] =
                    InstanceOfCounts.TryGetValue((TypeSymbol)x.AsType.Type, out int count)
                        ? count + 1
                        : 1;
            }

            return base.VisitInstanceOf(x);
        }

        public override VoidStruct VisitArrayItem(BoundArrayItemEx x)
        {
            if (x.Array is BoundVariableRef { Variable: ParameterReference _ })
            {
                ArrayAccessedParameters++;
            }

            return base.VisitArrayItem(x);
        }

        public override VoidStruct VisitFieldRef(BoundFieldRef x)
        {
            if (x.Instance is BoundVariableRef { Variable: ParameterReference _ })
            {
                FieldAccessedParameters++;
            }

            return base.VisitFieldRef(x);
        }

        public override VoidStruct VisitInstanceFunctionCall(BoundInstanceFunctionCall x)
        {
            if (x.Name.IsDirect && x.Instance is BoundVariableRef { Variable: ParameterReference _ })
            {
                MethodInvokedParameters++;

                string name = x.Name.NameValue.Name.Value;
                var candidates =
                    _compilation.SourceSymbolCollection.AllRoutines
                        .Where(r => r is SourceMethodSymbol && r.Name == name);
                if (candidates.Count() == 1)
                {
                    UniqueMethodInvokedParameters++;
                }
            }

            return base.VisitInstanceFunctionCall(x);
        }

        public override VoidStruct VisitConcat(BoundConcatEx x)
        {
            foreach (var arg in x.ArgumentsInSourceOrder)
            {
                if (arg.Value is BoundVariableRef { Variable: ParameterReference _ })
                {
                    ConcatenatedParameters++;
                }
            }

            return base.VisitConcat(x);
        }

        public override VoidStruct VisitBinaryExpression(BoundBinaryEx x)
        {
            if (x.Operation == Operations.Identical || x.Operation != Operations.Identical)
            {
                if (x.Left is BoundVariableRef { Variable: ParameterReference _ }
                    || x.Right is BoundVariableRef { Variable: ParameterReference _ } s)
                {
                    EqualityComparedParameters++;
                }
            }

            return base.VisitBinaryExpression(x);
        }

        public override VoidStruct VisitConversion(BoundConversionEx x)
        {
            if (x.Operand is BoundVariableRef { Variable: ParameterReference _ })
            {
                ConvertedParameters++;
            }

            return base.VisitConversion(x);
        }

        public override VoidStruct VisitIsSet(BoundIsSetEx x)
        {
            if (x.VarReference is BoundVariableRef { Variable: ParameterReference _ })
            {
                IssetParameters++;
            }

            return base.VisitIsSet(x);
        }

        private void NoteCheckedParameter(ParameterSymbol param)
        {
            if (!_routineCheckedParameters.Add(param))
            {
                TwiceTypeCheckedParameters++;
            }
        }
    }
}
