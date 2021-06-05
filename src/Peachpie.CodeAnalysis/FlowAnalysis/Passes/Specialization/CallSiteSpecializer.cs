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
    internal class CallSiteSpecializer : CommonRoutineSpecializer
    {
        public CallSiteSpecializer(PhpCompilation compilation) : base(compilation)
        {}

        protected override void GatherSpecializations(CallGraph callGraph, SourceFunctionSymbol function, SpecializationSet specializations)
        {
            var parameters = function.SourceParameters;

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
                            && SpecializationUtils.IsSpecializationEnabled(Compilation.Options.ExperimentalOptimization, argSpec))
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
        }

        private SpecializedParam TryGetArgumentSpecialization(TypeRefContext typeCtx, BoundExpression argExpr)
        {
            var argType = SpecializationUtils.EstimateExpressionType(Compilation, typeCtx, argExpr);

            var specializationFlags = GetSpecializationFlags(Compilation, typeCtx, argExpr);
            if ((specializationFlags & SpecializationFlags.IsNull) != 0)
            {
                argType = Compilation.CoreTypes.Object;
            }

            return new SpecializedParam(argType, specializationFlags);
        }

        private static SpecializationFlags GetSpecializationFlags(PhpCompilation compilation, TypeRefContext typeCtx, BoundExpression expr)
        {
            var flags = SpecializationFlags.None;

            var typeMask = SpecializationUtils.EstimateExpressionTypeRefMask(typeCtx, expr);
            if (typeCtx.IsNullOnly(typeMask))
            {
                flags |= SpecializationFlags.IsNull;
            } 

            return flags;
        }

        private static bool IsSpecialized(TypeSymbol paramType, TypeSymbol argType) =>
            paramType.Is_PhpValue() && !argType.Is_PhpValue() && !argType.Is_PhpAlias() && argType.SpecialType != SpecialType.System_Void && !argType.IsErrorType();
    }
}
