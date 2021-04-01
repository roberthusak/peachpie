using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Devsense.PHP.Syntax.Ast;
using Microsoft.CodeAnalysis;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.Utilities;

#nullable enable

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    [Flags]
    internal enum ParameterUsageFlags
    {
        None = 0,
        NullCheck = 1 << 0,
        EmptyCheck = 1 << 1,
        IssetCheck = 1 << 2,
        CallableCheck = 1 << 3,
        NumericCheck = 1 << 4,
        ArrayItemAccess = 1 << 5,
        PassedToConcat = 1 << 6,
        PassedToSubroutine = 1 << 7
    }

    internal class ParameterUsageInfo
    {
        public ParameterUsageFlags Flags { get; set; }

        public HashSet<TypeSymbol> TypeChecks { get; } = new HashSet<TypeSymbol>();

        public HashSet<string> AccessedFields { get; } = new HashSet<string>();

        public HashSet<string> CalledMethods { get; } = new HashSet<string>();
    }

    internal class ParameterUsageAnalyzer : GraphExplorer<VoidStruct>
    {
        private readonly SourceRoutineSymbol _routine;

        private CoreTypes CoreTypes => _routine.DeclaringCompilation.CoreTypes;

        private readonly Dictionary<ParameterSymbol, ParameterUsageInfo> _paramInfos =
            new Dictionary<ParameterSymbol, ParameterUsageInfo>();

        private ParameterUsageAnalyzer(SourceRoutineSymbol routine)
        {
            _routine = routine;
        }

        public static ImmutableArray<ParameterUsageInfo> AnalyseParameterUsages(SourceRoutineSymbol routine)
        {
            Debug.Assert(routine.ControlFlowGraph != null);

            var analyser = new ParameterUsageAnalyzer(routine);
            foreach (var param in routine.SourceParameters)
            {
                analyser._paramInfos[param] = new ParameterUsageInfo();
            }

            analyser.VisitCFG(routine.ControlFlowGraph);

            return
                routine.SourceParameters
                    .Select(p => analyser._paramInfos[p])
                    .ToImmutableArray();
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
                        case "is_bool":
                            _paramInfos[paramRef.Parameter].TypeChecks.Add(CoreTypes.Boolean);
                            break;

                        case "is_int":
                        case "is_integer":
                        case "is_long":
                            _paramInfos[paramRef.Parameter].TypeChecks.Add(CoreTypes.Long);
                            break;

                        case "is_float":
                        case "is_double":
                        case "is_real":
                            _paramInfos[paramRef.Parameter].TypeChecks.Add(CoreTypes.Double);
                            break;

                        case "is_string":
                            _paramInfos[paramRef.Parameter].TypeChecks.Add(CoreTypes.PhpString);
                            break;

                        case "is_resource":
                            _paramInfos[paramRef.Parameter].TypeChecks.Add(CoreTypes.PhpResource);
                            break;

                        case "is_array":
                            _paramInfos[paramRef.Parameter].TypeChecks.Add(CoreTypes.PhpArray);
                            break;

                        case "is_object":
                            // TODO: Exclude resources somehow
                            _paramInfos[paramRef.Parameter].TypeChecks.Add(CoreTypes.Object);
                            break;

                        case "is_null":
                            _paramInfos[paramRef.Parameter].Flags |= ParameterUsageFlags.NullCheck;
                            break;

                        case "empty":
                            _paramInfos[paramRef.Parameter].Flags |= ParameterUsageFlags.EmptyCheck;
                            break;

                        case "is_numeric":
                            _paramInfos[paramRef.Parameter].Flags |= ParameterUsageFlags.NumericCheck;
                            break;

                        case "is_callable":
                            _paramInfos[paramRef.Parameter].Flags |= ParameterUsageFlags.CallableCheck;
                            break;
                    }
                }

                if (x.TargetMethod is SourceFunctionSymbol)
                {
                    foreach (var arg in x.ArgumentsInSourceOrder)
                    {
                        if (arg.Value is BoundVariableRef { Variable: ParameterReference paramRef2 })
                        {
                            _paramInfos[paramRef2.Parameter].Flags |= ParameterUsageFlags.PassedToSubroutine;
                        }
                    }
                }
            }

            return base.VisitGlobalFunctionCall(x);
        }

        public override VoidStruct VisitInstanceOf(BoundInstanceOfEx x)
        {
            if (x.AsType.Type is TypeSymbol type && !type.IsErrorType() 
                && x.Operand is BoundVariableRef { Variable: ParameterReference paramRef })
            {
                _paramInfos[paramRef.Parameter].TypeChecks.Add(GeneralizeParameterType(type));
            }

            return base.VisitInstanceOf(x);
        }

        public override VoidStruct VisitArrayItem(BoundArrayItemEx x)
        {
            if (x.Array is BoundVariableRef { Variable: ParameterReference paramRef })
            {
                _paramInfos[paramRef.Parameter].Flags |= ParameterUsageFlags.ArrayItemAccess;
            }

            return base.VisitArrayItem(x);
        }

        public override VoidStruct VisitFieldRef(BoundFieldRef x)
        {
            if (x.FieldName.IsDirect && x.Instance is BoundVariableRef { Variable: ParameterReference paramRef })
            {
                _paramInfos[paramRef.Parameter].AccessedFields.Add(x.FieldName.NameValue.Value);
            }

            return base.VisitFieldRef(x);
        }

        public override VoidStruct VisitInstanceFunctionCall(BoundInstanceFunctionCall x)
        {
            if (x.Name.IsDirect && x.Instance is BoundVariableRef { Variable: ParameterReference paramRef })
            {
                _paramInfos[paramRef.Parameter].CalledMethods.Add(x.Name.NameValue.Name.Value);
            }

            return base.VisitInstanceFunctionCall(x);
        }

        public override VoidStruct VisitConcat(BoundConcatEx x)
        {
            foreach (var arg in x.ArgumentsInSourceOrder)
            {
                if (arg.Value is BoundVariableRef { Variable: ParameterReference paramRef })
                {
                    _paramInfos[paramRef.Parameter].Flags |= ParameterUsageFlags.PassedToConcat;
                }
            }

            return base.VisitConcat(x);
        }

        public override VoidStruct VisitBinaryExpression(BoundBinaryEx x)
        {
            if (x.Operation == Operations.Identical || x.Operation != Operations.Identical)
            {
                CheckIdentityComparison(x.Left, x.Right);
                CheckIdentityComparison(x.Right, x.Left);
            }

            return base.VisitBinaryExpression(x);

            void CheckIdentityComparison(BoundExpression paramOperand, BoundExpression valueOperand)
            {
                if (paramOperand is BoundVariableRef { Variable: ParameterReference param }
                    && TryGetSpecificType(valueOperand, out var type))
                {
                    _paramInfos[param.Parameter].TypeChecks.Add(GeneralizeParameterType(type));

                    if (valueOperand.ConstantValue.IsNull())
                    {
                        _paramInfos[param.Parameter].Flags |= ParameterUsageFlags.NullCheck;
                    }
                }
            }
        }

        public override VoidStruct VisitIsSet(BoundIsSetEx x)
        {
            if (x.VarReference is BoundVariableRef { Variable: ParameterReference paramRef })
            {
                _paramInfos[paramRef.Parameter].Flags |= ParameterUsageFlags.IssetCheck;
            }

            return base.VisitIsSet(x);
        }

        private bool TryGetSpecificType(BoundExpression expr, out TypeSymbol type)
        {
            type = SpecializationUtils.EstimateExpressionType(_routine.DeclaringCompilation, _routine.TypeRefContext, expr);
            return !type.Is_PhpValue() && !type.Is_PhpAlias() && !type.IsVoid() && !type.IsErrorType();
        }

        private TypeSymbol GeneralizeParameterType(TypeSymbol type)
        {
            // TODO: Maybe unify with CallSiteSpecializer.GeneralizeParameterType
            // TODO: Handle other types as well if useful (e.g. PhpNumber)
            if (type.SpecialType == SpecialType.System_String)
            {
                return _routine.DeclaringCompilation.CoreTypes.PhpString;
            }
            else
            {
                return type;
            }
        }
    }
}
