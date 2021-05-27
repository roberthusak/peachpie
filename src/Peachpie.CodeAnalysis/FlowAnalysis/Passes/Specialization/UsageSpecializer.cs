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
    internal class UsageSpecializer : IRoutineSpecializer
    {
        private readonly PhpCompilation _compilation;

        private readonly Dictionary<SourceRoutineSymbol, SpecializationSet> _specializations =
            new Dictionary<SourceRoutineSymbol, SpecializationSet>();

        public UsageSpecializer(PhpCompilation compilation)
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

                    var paramInfos = ParameterUsageAnalyzer.AnalyseParameterUsages(function);
                    Debug.Assert(parameters.Length == paramInfos.Length);

                    bool isSpecialized = false;
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var parameter = parameters[i];
                        if (parameter.Type.Is_PhpValue() && TryGetParameterTypeVariants(parameter, paramInfos[i], out var types))
                        {
                            isSpecialized = true;
                            specializations.AddParameterTypeVariants(types);
                        }
                        else
                        {
                            specializations.AddParameterTypeVariants(new[] { parameter.Type });
                        }
                    }

                    if (isSpecialized)
                    {
                        _specializations[function] = specializations;
                    }
                }
            }
        }

        private bool TryGetParameterTypeVariants(SourceParameterSymbol parameter, ParameterUsageInfo paramInfo, out HashSet<TypeSymbol> types)
        {
            if (parameter.IsParams)
            {
                types = null;
                return false;
            }

            types = new HashSet<TypeSymbol>(paramInfo.TypeChecks);

            if (paramInfo.AccessedFields.Count > 0 || paramInfo.CalledMethods.Count > 0)
            {
                // TODO: Consider checking imported types as well
                var candidateTypes = new HashSet<SourceTypeSymbol>(_compilation.SourceSymbolCollection.GetTypes());

                if (paramInfo.CalledMethods.Count > 0)
                {
                    candidateTypes.RemoveWhere(type =>
                        !paramInfo.CalledMethods.All(methodName =>
                            type.GetMembers(methodName).FirstOrDefault() is MethodSymbol));
                }

                if (paramInfo.AccessedFields.Count > 0)
                {
                    candidateTypes.RemoveWhere(type =>
                        !paramInfo.AccessedFields.All(fieldName =>
                            type.GetMembers(fieldName).FirstOrDefault() is FieldSymbol));
                }

                if (candidateTypes.Count > 0 && candidateTypes.Count <= 4)
                {
                    types.UnionWith(candidateTypes);
                }
                else
                {
                    types.Add(_compilation.CoreTypes.Object);
                }
            }

            if ((paramInfo.Flags & ParameterUsageFlags.ArrayItemAccess) != 0)
            {
                types.Add(_compilation.CoreTypes.PhpArray);
            }

            if ((paramInfo.Flags & ParameterUsageFlags.PassedToConcat) != 0)
            {
                types.Add(_compilation.CoreTypes.PhpString);
            }

            if ((paramInfo.Flags & ParameterUsageFlags.NullCheck) != 0 &&
                !types.Any(type => type.IsReferenceType))
            {
                types.Add(_compilation.CoreTypes.Object);
            }

            types.RemoveWhere(type =>
                !SpecializationUtils.IsSpecializationEnabled(_compilation.Options.ExperimentalOptimization, type));

            return types.Count > 0;
        }

        public bool TryGetRoutineSpecializedParameters(SourceRoutineSymbol routine, out SpecializationSet specializations) =>
            _specializations.TryGetValue(routine, out specializations);
    }
}
