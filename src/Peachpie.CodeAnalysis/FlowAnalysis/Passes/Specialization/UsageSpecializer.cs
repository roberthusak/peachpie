using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.FlowAnalysis.Graph;
using Peachpie.CodeAnalysis.Utilities;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    internal class UsageSpecializer : CommonRoutineSpecializer
    {
        public UsageSpecializer(PhpCompilation compilation) : base(compilation)
        {}

        public override void GatherSpecializations(CallGraph callGraph, SourceFunctionSymbol function, SpecializationSet specializations)
        {
            var parameters = function.SourceParameters;

            var paramInfos = ParameterUsageAnalyzer.AnalyseParameterUsages(function);
            Debug.Assert(parameters.Length == paramInfos.Length);

            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter.Type.Is_PhpValue() && TryGetParameterTypeVariants(parameter, paramInfos[i], out var types))
                {
                    types.Add(parameter.Type);
                    specializations.AddParameterTypeVariants(types);
                }
                else
                {
                    specializations.AddParameterTypeVariants(new[] { (SpecializedParam)parameter.Type });
                }
            }
        }

        private bool TryGetParameterTypeVariants(SourceParameterSymbol parameter, ParameterUsageInfo paramInfo, out HashSet<SpecializedParam> types)
        {
            if (parameter.IsParams)
            {
                types = null;
                return false;
            }

            types = new HashSet<SpecializedParam>(paramInfo.TypeChecks.Select(type => (SpecializedParam)type));

            if (paramInfo.AccessedFields.Count > 0 || paramInfo.CalledMethods.Count > 0)
            {
                // TODO: Consider checking imported types as well
                var candidateTypes = new HashSet<SpecializedParam>(
                    Compilation.SourceSymbolCollection.GetTypes()
                        .Select(type => (SpecializedParam)type));

                if (paramInfo.CalledMethods.Count > 0)
                {
                    candidateTypes.RemoveWhere(type =>
                        !paramInfo.CalledMethods.All(methodName =>
                            type.Type.GetMembers(methodName).FirstOrDefault() is MethodSymbol));
                }

                if (paramInfo.AccessedFields.Count > 0)
                {
                    candidateTypes.RemoveWhere(type =>
                        !paramInfo.AccessedFields.All(fieldName =>
                            type.Type.GetMembers(fieldName).FirstOrDefault() is FieldSymbol));
                }

                if (candidateTypes.Count > 0 && candidateTypes.Count <= 4)
                {
                    types.UnionWith(candidateTypes);
                }
                else
                {
                    types.Add(Compilation.CoreTypes.Object.Symbol);
                }
            }

            if ((paramInfo.Flags & ParameterUsageFlags.ArrayItemAccess) != 0)
            {
                types.Add(Compilation.CoreTypes.PhpArray.Symbol);
            }

            if ((paramInfo.Flags & ParameterUsageFlags.PassedToConcat) != 0)
            {
                types.Add(Compilation.CoreTypes.String.Symbol);
            }

            if ((paramInfo.Flags & ParameterUsageFlags.NullCheck) != 0 &&
                !types.Any(type => type.Type.IsReferenceType))
            {
                types.Add(new SpecializedParam(Compilation.CoreTypes.Object, SpecializationFlags.IsNull));
            }

            types.RemoveWhere(type =>
                !SpecializationUtils.IsSpecializationEnabled(Compilation.Options.ExperimentalOptimization, type));

            return types.Count > 0;
        }
    }
}
