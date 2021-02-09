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
                    bool isSpecialized = false;
                    var paramTypes =
                        function.SourceParameters
                            .Select(p => p.Type)
                            .ToArray();

                    foreach (var call in callGraph.GetCallerEdges(function))
                    {
                        var args = call.CallSite.CallExpression.ArgumentsInSourceOrder;
                        for (int i = 0; i < args.Length; i++)
                        {
                            var argType = GeneralizeParameterType(
                                SpecializationUtils.EstimateExpressionType(_compilation, call.Caller.TypeRefContext, args[i].Value));
                            if (i < paramTypes.Length && IsSpecialized(paramTypes[i], argType))
                            {
                                paramTypes[i] = argType;
                                isSpecialized = true;

                                // TODO: Consider the most common specialization, not just the first one as now
                            }
                        }

                        // Default values of not passed arguments
                        for (int i = args.Length; i < function.SourceParameters.Length; i++)
                        {
                            var param = function.SourceParameters[i];
                            if (param.Initializer != null)
                            {
                                var defaultType = SpecializationUtils.EstimateExpressionType(_compilation, function.TypeRefContext, param.Initializer);
                                if (IsSpecialized(paramTypes[i], defaultType))
                                {
                                    paramTypes[i] = defaultType;
                                    isSpecialized = true;
                                }
                            }
                        }
                    }

                    if (isSpecialized)
                    {
                        _specializations[function] = paramTypes.ToImmutableArray();
                    }
                }
            }
        }

        private static bool IsSpecialized(TypeSymbol paramType, TypeSymbol argType) =>
            paramType.Is_PhpValue() && !argType.Is_PhpAlias() && argType.SpecialType != SpecialType.System_Void && !argType.IsErrorType();

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
