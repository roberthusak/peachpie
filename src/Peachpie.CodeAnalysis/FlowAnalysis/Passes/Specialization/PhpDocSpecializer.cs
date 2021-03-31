using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Devsense.PHP.Syntax.Ast;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.FlowAnalysis;
using Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.FlowAnalysis.Graph;
using Peachpie.CodeAnalysis.Utilities;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    class PhpDocSpecializer : IRoutineSpecializer
    {
        private readonly PhpCompilation _compilation;

        private Dictionary<SourceRoutineSymbol, SpecializationSet> _specializations;

        public PhpDocSpecializer(PhpCompilation compilation)
        {
            _compilation = compilation;
        }

        public void OnAfterAnalysis(CallGraph callGraph)
        {
            if (_specializations == null)
            {
                _specializations = new Dictionary<SourceRoutineSymbol, SpecializationSet>();
                foreach (var function in _compilation.SourceSymbolCollection.GetFunctions())
                {
                    var parameters = function.SourceParameters;
                    if (parameters.Length > 0)
                    {
                        var specializations = SpecializationSet.CreateEmpty();

                        bool isSpecialized = false;
                        foreach (var parameter in parameters)
                        {
                            if (parameter.Type.Is_PhpValue() && parameter.TryGetTypesFromPhpDoc(out var types))
                            {
                                isSpecialized = true;
                                specializations.AddParameterTypeVariants(types);
                            }
                            else
                            {
                                specializations.AddParameterTypeVariants(new []{parameter.Type});
                            }
                        }

                        if (isSpecialized)
                        {
                            _specializations.Add(function, specializations);
                        }
                    }

                }
            }
        }

        public bool TryGetRoutineSpecializedParameters(SourceRoutineSymbol routine, out SpecializationSet specializations) =>
            _specializations.TryGetValue(routine, out specializations);
    }
}
