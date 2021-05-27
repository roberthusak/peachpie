using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.Utilities;
using Roslyn.Utilities;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    internal readonly struct SpecializationSet
    {
        public SortedSet<ImmutableArray<SpecializedParam>> Set { get; }

        private SpecializationSet(SortedSet<ImmutableArray<SpecializedParam>> set)
        {
            Set = set;
        }

        public SpecializationSet(ImmutableArray<SpecializedParam> specialization)
            : this(CreateEmptySet())
        {
            Set.Add(specialization);
        }

        public void AddParameterTypeVariants(IEnumerable<TypeSymbol> types) =>
            AddParameterTypeVariants(types.Select(t => new SpecializedParam(t)));

        public void AddParameterTypeVariants(IEnumerable<SpecializedParam> types)
        {
            if (Set.Count == 0)
            {
                var typeArrays = types.Select(ImmutableArray.Create);
                Set.AddAll(typeArrays);
            }
            else
            {
                var typeArrays = 
                    (from parameters in Set
                    from type in types
                    select parameters.Add(type))
                        .ToArray();

                Set.Clear();
                Set.AddAll(typeArrays);
            }
        }

        public static SpecializationSet CreateEmpty() =>
            new SpecializationSet(CreateEmptySet());

        private static SortedSet<ImmutableArray<SpecializedParam>> CreateEmptySet() =>
            new SortedSet<ImmutableArray<SpecializedParam>>(SpecializationUtils.SpecializationComparer);
    }
}
