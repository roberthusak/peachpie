using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.FlowAnalysis.Graph;
using Peachpie.CodeAnalysis.Utilities;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    class TargetedSpecializer : CommonRoutineSpecializer
    {
        private readonly CallSiteSpecializer _callSiteSpecializer;
        private readonly UsageSpecializer _usageSpecializer;

        public TargetedSpecializer(PhpCompilation compilation) : base(compilation)
        {
            _callSiteSpecializer = new CallSiteSpecializer(compilation);
            _usageSpecializer = new UsageSpecializer(compilation);
        }

        public override void GatherSpecializations(CallGraph callGraph, SourceFunctionSymbol function, SpecializationSet specializations)
        {
            var callSiteSpecs = SpecializationSet.CreateEmpty();
            _callSiteSpecializer.GatherSpecializations(callGraph, function, callSiteSpecs);

            var usageSpecs = SpecializationSet.CreateEmpty();
            _usageSpecializer.GatherSpecializations(callGraph, function, usageSpecs);

            callSiteSpecs.Set.IntersectWith(usageSpecs.Set);

            specializations.Set.UnionWith(callSiteSpecs.Set);
        }
    }
}
