using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.FlowAnalysis.Graph;
using Peachpie.CodeAnalysis.Utilities;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    internal class CallSiteSpecializer : IRoutineSpecializer
    {
        private readonly PhpCompilation _compilation;

        private readonly Dictionary<SourceRoutineSymbol, ImmutableArray<TypeSymbol>> _specializations =
            new Dictionary<SourceRoutineSymbol, ImmutableArray<TypeSymbol>>();

        public CallSiteSpecializer(PhpCompilation compilation)
        {
            _compilation = compilation;
        }

        public void OnAfterAnalysis(CallGraph callGraph)
        {
            foreach (var function in _compilation.SourceSymbolCollection.GetFunctions())
            {
                if (function.SourceParameters.Length > 0 && !_specializations.ContainsKey(function))
                { 
                    var parameters = function.SourceParameters;

                    // Discover what types have the arguments passed to the function parameters
                    var paramStats = new Dictionary<TypeSymbol, int>[function.SourceParameters.Length];
                    foreach (var call in callGraph.GetCallerEdges(function))
                    {
                        var args = call.CallSite.CallExpression.ArgumentsInSourceOrder;

                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var (typeCtx, argExpr) =
                                (i < args.Length)
                                    ? (call.Caller.TypeRefContext, args[i].Value)               // Existing parameter
                                    : (function.TypeRefContext, parameters[i].Initializer);     // Default value

                            if (argExpr != null)
                            {
                                var argType = GeneralizeParameterType(
                                    SpecializationUtils.EstimateExpressionType(_compilation, typeCtx, argExpr));
                                if (IsSpecialized(parameters[i].Type, argType))
                                {
                                    NoteParameterType(paramStats, i, argType);
                                }
                            }
                        }
                    }

                    // Determine the specialized parameter types
                    var paramTypes = new TypeSymbol[parameters.Length];
                    bool isSpecialized = false;
                    for (int i = 0; i < paramTypes.Length; i++)
                    {
                        // Select the most common specialized type if present
                        paramTypes[i] =
                            (paramStats[i]?.Count > 0)
                                ? paramStats[i].Aggregate((a, b) => a.Value >= b.Value ? a : b).Key
                                : parameters[i].Type;

                        isSpecialized |= (paramTypes[i] != parameters[i].Type);
                    }

                    if (isSpecialized)
                    {
                        _specializations[function] = paramTypes.ToImmutableArray();
                    }
                }
            }

            void NoteParameterType(Dictionary<TypeSymbol, int>[] paramStats, int paramIndex, TypeSymbol type)
            {
                if (paramIndex >= paramStats.Length)
                {
                    return;
                }

                paramStats[paramIndex] ??= new Dictionary<TypeSymbol, int>();

                var types = paramStats[paramIndex];
                if (types.TryGetValue(type, out int currentCount))
                {
                    types[type] = currentCount + 1;
                }
                else
                {
                    types[type] = 1;
                }
            }
        }

        private static bool IsSpecialized(TypeSymbol paramType, TypeSymbol argType) =>
            paramType.Is_PhpValue() && !argType.Is_PhpValue() && !argType.Is_PhpAlias() && argType.SpecialType != SpecialType.System_Void && !argType.IsErrorType();

        private TypeSymbol GeneralizeParameterType(TypeSymbol type)
        {
            // TODO: Handle other types as well if useful (e.g. PhpNumber)
            if (type.SpecialType == SpecialType.System_String)
            {
                return _compilation.CoreTypes.PhpString;
            }
            else
            {
                return type;
            }
        }

        public bool TryGetRoutineSpecializedParameters(SourceRoutineSymbol routine, out ImmutableArray<TypeSymbol> parameterTypes) =>
            _specializations.TryGetValue(routine, out parameterTypes);
    }
}
