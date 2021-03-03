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
                    var paramInfos = ParameterUsageAnalyzer.AnalyseParameterUsages(function);

                    Debug.Assert(parameters.Length == paramInfos.Length);
                    var paramTypes = new TypeSymbol[paramInfos.Length];
                    bool isSpecialized = false;

                    for (int i = 0; i < paramInfos.Length; i++)
                    {
                        paramTypes[i] = TrySpecializeParameter(parameters[i], paramInfos[i]);
                        isSpecialized |= (paramTypes[i] != parameters[i].Type); 
                    }

                    if (isSpecialized)
                    {
                        var specParams = paramTypes.ToImmutableArray();
                        _specializations[function] = new SpecializationSet(specParams);
                    }
                }
            }
        }

        private TypeSymbol TrySpecializeParameter(SourceParameterSymbol parameter, ParameterUsageInfo paramInfo)
        {
            if (parameter.IsParams)
            {
                return parameter.Type;
            }

            if (paramInfo.TypeChecks.Count == 1)
            {
                return paramInfo.TypeChecks.Single();
            }

            if (paramInfo.AccessedFields.Count > 0 || paramInfo.CalledMethods.Count > 0)
            {
                // TODO: Consider checking imported types as well
                var candidateTypes = new HashSet<SourceTypeSymbol>(_compilation.SourceSymbolCollection.GetTypes());

                if (paramInfo.CalledMethods.Count > 0)
                {
                    candidateTypes.RemoveWhere(type =>
                        !paramInfo.CalledMethods.Any(methodName =>
                            type.GetMembers(methodName).FirstOrDefault() is MethodSymbol));
                }

                if (paramInfo.AccessedFields.Count > 0)
                {
                    candidateTypes.RemoveWhere(type =>
                        !paramInfo.AccessedFields.Any(fieldName =>
                            type.GetMembers(fieldName).FirstOrDefault() is FieldSymbol));
                }

                if (candidateTypes.Count == 1)
                {
                    return candidateTypes.Single();
                }
                else
                {
                    return _compilation.CoreTypes.Object;
                }
            }

            if ((paramInfo.Flags & ParameterUsageFlags.ArrayItemAccess) != 0)
            {
                return _compilation.CoreTypes.PhpArray;
            }

            if ((paramInfo.Flags & ParameterUsageFlags.PassedToConcat) != 0)
            {
                return _compilation.CoreTypes.PhpString;
            }

            return parameter.Type;
        }

        public bool TryGetRoutineSpecializedParameters(SourceRoutineSymbol routine, out SpecializationSet specializations) =>
            _specializations.TryGetValue(routine, out specializations);
    }
}
