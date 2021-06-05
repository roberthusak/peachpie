using System;
using System.Collections.Generic;
using System.Text;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.FlowAnalysis.Graph;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    internal abstract class CommonRoutineSpecializer : IRoutineSpecializer
    {
        protected PhpCompilation Compilation { get; }

        private readonly Dictionary<SourceRoutineSymbol, SpecializationSet> _specializations =
            new Dictionary<SourceRoutineSymbol, SpecializationSet>();

        protected CommonRoutineSpecializer(PhpCompilation compilation)
        {
            this.Compilation = compilation;
        }

        public void OnAfterAnalysis(CallGraph callGraph)
        {
            foreach (var function in Compilation.SourceSymbolCollection.GetFunctions())
            {
                if (function.SourceParameters.Length == 0)
                    continue;;

                bool existing = _specializations.TryGetValue(function, out var functionSpecializations);
                if (!existing)
                    functionSpecializations = SpecializationSet.CreateEmpty();
                else if ((Compilation.Options.ExperimentalOptimization & ExperimentalOptimization.EnableIncrementalSpecialization) == 0)
                    continue;

                GatherSpecializations(callGraph, function, functionSpecializations);

                if (!existing && functionSpecializations.Set.Count > 0)
                    _specializations[function] = functionSpecializations;
            }
        }

        protected abstract void GatherSpecializations(CallGraph callGraph, SourceFunctionSymbol function, SpecializationSet specializations);

        public bool TryGetRoutineSpecializedParameters(SourceRoutineSymbol routine, out SpecializationSet specializations) =>
            _specializations.TryGetValue(routine, out specializations);
    }
}
