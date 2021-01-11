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
using Peachpie.CodeAnalysis.Utilities;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    class PhpDocSpecializer : IRoutineSpecializer
    {
        private readonly PhpCompilation _compilation;

        private Dictionary<SourceRoutineSymbol, ImmutableArray<TypeSymbol>> _specializations;

        public PhpDocSpecializer(PhpCompilation compilation)
        {
            _compilation = compilation;
        }

        public void OnAfterAnalysis()
        {
            if (_specializations == null)
            {
                _specializations = new Dictionary<SourceRoutineSymbol, ImmutableArray<TypeSymbol>>();
                foreach (var function in _compilation.SourceSymbolCollection.GetFunctions())
                {
                    if (function.SourceParameters.Any(CanBeParameterSpecialized))
                    {
                        var specParams =
                            function.SourceParameters
                                .Select(p => p.TryGetTypeFromPhpDoc(out var type) ? type : p.Type)
                                .ToImmutableArray();
                        _specializations.Add(function, specParams);
                    }
                }
            }

            static bool CanBeParameterSpecialized(SourceParameterSymbol parameter)
            {
                if (parameter.Type.Is_PhpValue() && parameter.TryGetTypeFromPhpDoc(out var type))
                {
                    return !type.Is_PhpValue();
                }

                return false;
            }
        }

        public bool TryGetRoutineSpecializedParameters(SourceRoutineSymbol routine, out ImmutableArray<TypeSymbol> parameterTypes) =>
            _specializations.TryGetValue(routine, out parameterTypes);
    }
}
