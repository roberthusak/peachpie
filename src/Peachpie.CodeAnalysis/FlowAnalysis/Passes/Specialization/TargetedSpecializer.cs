using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.FlowAnalysis.Graph;
using Peachpie.CodeAnalysis.Utilities;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    class TargetedSpecializer : IRoutineSpecializer
    {
        private readonly PhpCompilation _compilation;

        private readonly Dictionary<SourceRoutineSymbol, SpecializationSet> _specializations =
            new Dictionary<SourceRoutineSymbol, SpecializationSet>();

        public TargetedSpecializer(PhpCompilation compilation)
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
                    var paramInfos = ParameterUsageAnalyzer.AnalyseParameterUsages(function);
                    Debug.Assert(parameters.Length == paramInfos.Length);

                    var specializations = SpecializationSet.CreateEmpty();

                    bool isSpecialized = false;
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var parameter = parameters[i];
                        var paramInfo = paramInfos[i];

                        if (parameter.Type.Is_PhpValue()
                            && ((paramInfo.Flags & ParameterUsageFlags.PassedToConcat) != 0
                                || paramInfo.TypeChecks.Contains(_compilation.CoreTypes.String))
                            && (_compilation.Options.ExperimentalOptimization & ExperimentalOptimization.SpecializeString) != 0
                            && IsArgumentTypePassed(_compilation.CoreTypes.String))
                        {
                            isSpecialized = true;
                            specializations.AddParameterTypeVariants(new [] { parameter.Type, _compilation.CoreTypes.String });
                        }
                        else
                        {
                            specializations.AddParameterTypeVariants(new [] { parameter.Type });
                        }

                        bool IsArgumentTypePassed(TypeSymbol type)
                        {
                            foreach (var call in callGraph.GetCallerEdges(function))
                            {
                                var args = call.CallSite.CallExpression.ArgumentsInSourceOrder;
                                var (typeCtx, argExpr) =
                                    (i < args.Length)
                                        ? (call.Caller.TypeRefContext, args[i].Value)               // Existing parameter
                                        : (function.TypeRefContext, parameter.Initializer);         // Default value

                                if (argExpr != null &&
                                    SpecializationUtils.EstimateExpressionType(_compilation, typeCtx, argExpr).Equals(type))
                                {
                                    return true;
                                }
                            }

                            return false;
                        }
                    }

                    if (isSpecialized)
                    {
                        // Remove the variant with no specializations
                        specializations.Set.RemoveWhere(types =>
                            types.SequenceEqual(from parameter in parameters select parameter.Type));

                        _specializations[function] = specializations;
                    }
                }
            }
        }

        public bool TryGetRoutineSpecializedParameters(SourceRoutineSymbol routine, out SpecializationSet specializations) =>
            _specializations.TryGetValue(routine, out specializations);
    }
}
