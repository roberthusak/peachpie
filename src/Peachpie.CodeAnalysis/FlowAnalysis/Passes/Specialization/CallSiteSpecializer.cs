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
                        var argSpecs = new SpecializedParam[parameters.Length];

                        bool isSpecialized = false;
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var parameter = parameters[i];

                            var (typeCtx, argExpr) =
                                (i < args.Length)
                                    ? (call.Caller.TypeRefContext, args[i].Value)               // Existing parameter
                                    : (function.TypeRefContext, parameter.Initializer);         // Default value

                            argSpecs[i] = parameter.Type;
                            if (argExpr != null)
                            {
                                var argSpec = TryGetArgumentSpecialization(typeCtx, argExpr);

                                if (IsSpecialized(parameter.Type, argSpec.Type)
                                    && SpecializationUtils.IsSpecializationEnabled(_compilation.Options.ExperimentalOptimization, argSpec))
                                {
                                    argSpecs[i] = argSpec;
                                    isSpecialized = true;
                                }
                            }
                        }

                        if (isSpecialized)
                        {
                            specializations.Set.Add(argSpecs.ToImmutableArray());
                        }
                    }

                    if (specializations.Set.Count > 0)
                    {
                        _specializations[function] = specializations;
                    }
                }
            }
        }

        private SpecializedParam TryGetArgumentSpecialization(TypeRefContext typeCtx, BoundExpression argExpr)
        {
            var argType = SpecializationUtils.EstimateExpressionType(_compilation, typeCtx, argExpr);

            var specializationFlags = GetSpecializationFlags(_compilation, typeCtx, argExpr);
            if ((specializationFlags & SpecializationFlags.IsNull) != 0)
            {
                argType = _compilation.CoreTypes.Object;
            }

            return new SpecializedParam(argType, specializationFlags);
        }

        private static SpecializationFlags GetSpecializationFlags(PhpCompilation compilation, TypeRefContext typeCtx, BoundExpression expr)
        {
            var flags = SpecializationFlags.None;

            var typeMask = SpecializationUtils.EstimateExpressionTypeRefMask(typeCtx, expr);
            if (typeCtx.IsNull(typeMask))
            {
                flags |= SpecializationFlags.IsNull;
            }

            return flags;
        }

        private static bool IsSpecialized(TypeSymbol paramType, TypeSymbol argType) =>
            paramType.Is_PhpValue() && !argType.Is_PhpValue() && !argType.Is_PhpAlias() && argType.SpecialType != SpecialType.System_Void && !argType.IsErrorType();

        public bool TryGetRoutineSpecializedParameters(SourceRoutineSymbol routine, out SpecializationSet specializations) =>
            _specializations.TryGetValue(routine, out specializations);
    }
}
