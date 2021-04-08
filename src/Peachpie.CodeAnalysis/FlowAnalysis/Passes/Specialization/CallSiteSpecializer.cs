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

        private readonly Dictionary<SourceRoutineSymbol, SpecializationSet> _specializations =
            new Dictionary<SourceRoutineSymbol, SpecializationSet>();

        public CallSiteSpecializer(PhpCompilation compilation)
        {
            _compilation = compilation;
        }

        public void OnAfterAnalysis(CallGraph callGraph)
        {
            foreach (var function in _compilation.SourceSymbolCollection.GetFunctions())
            {
                var parameters = function.SourceParameters;
                if (parameters.Length > 0 && !_specializations.ContainsKey(function))
                {
                    var specializations = SpecializationSet.CreateEmpty();

                    foreach (var call in callGraph.GetCallerEdges(function))
                    {
                        var args = call.CallSite.CallExpression.ArgumentsInSourceOrder;
                        var argTypes = new TypeSymbol[parameters.Length];

                        bool isSpecialized = false;
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var parameter = parameters[i];

                            var (typeCtx, argExpr) =
                                (i < args.Length)
                                    ? (call.Caller.TypeRefContext, args[i].Value)               // Existing parameter
                                    : (function.TypeRefContext, parameter.Initializer);         // Default value

                            argTypes[i] = parameter.Type;
                            if (argExpr != null)
                            {
                                var argType = SpecializationUtils.EstimateExpressionType(_compilation, typeCtx, argExpr);
                                if (IsSpecialized(parameter.Type, argType)
                                    && SpecializationUtils.IsTypeSpecializationEnabled(_compilation.Options.ExperimentalOptimization, argType))
                                {
                                    argTypes[i] = argType;
                                    isSpecialized = true;
                                }
                            }
                        }

                        if (isSpecialized)
                        {
                            specializations.Set.Add(argTypes.ToImmutableArray());
                        }
                    }

                    if (specializations.Set.Count > 0)
                    {
                        _specializations[function] = specializations;
                    }
                }
            }
        }

        private static bool IsSpecialized(TypeSymbol paramType, TypeSymbol argType) =>
            paramType.Is_PhpValue() && !argType.Is_PhpValue() && !argType.Is_PhpAlias() && argType.SpecialType != SpecialType.System_Void && !argType.IsErrorType();

        public bool TryGetRoutineSpecializedParameters(SourceRoutineSymbol routine, out SpecializationSet specializations) =>
            _specializations.TryGetValue(routine, out specializations);
    }
}
