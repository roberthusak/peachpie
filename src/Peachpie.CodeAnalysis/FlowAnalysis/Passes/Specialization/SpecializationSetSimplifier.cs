using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.FlowAnalysis.Graph;
using Peachpie.CodeAnalysis.Utilities;

namespace Peachpie.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    class SpecializationSetSimplifier : CommonRoutineSpecializer
    {
        private readonly CommonRoutineSpecializer _specializer;

        public SpecializationSetSimplifier(PhpCompilation compilation, CommonRoutineSpecializer specializer) : base(compilation)
        {
            _specializer = specializer;
        }

        public override void GatherSpecializations(CallGraph callGraph, SourceFunctionSymbol function, SpecializationSet specializations)
        {
            var wholeSet = SpecializationSet.CreateEmpty();
            _specializer.GatherSpecializations(callGraph, function, wholeSet);
            
            if (wholeSet.Set.Count == 0)
            {
                return;
            }

            // TODO: Optimize performance of this query if necessary
            var bestSpecialization =
                Enumerable.Range(0, function.SourceParameters.Length)
                    .Select(i =>
                        wholeSet.Set
                            .Select(paramTypes => paramTypes[i])
                            .OrderBy(t => t, SpecializationUtils.SpecializedParameterComparer)
                            .First()
                    )
                    .ToImmutableArray();

            var worstSpecialization =
                Enumerable.Range(0, function.SourceParameters.Length)
                    .Select(i =>
                        wholeSet.Set
                            .Select(paramTypes => paramTypes[i])
                            .OrderBy(t => t, SpecializationUtils.SpecializedParameterComparer)
                            .Last()
                    )
                    .ToImmutableArray();

            specializations.Set.Add(bestSpecialization);
            specializations.Set.Add(worstSpecialization);
        }
    }
}
