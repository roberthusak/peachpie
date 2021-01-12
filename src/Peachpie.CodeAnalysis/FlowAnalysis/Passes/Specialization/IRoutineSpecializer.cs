using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.FlowAnalysis.Graph;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    internal interface IRoutineSpecializer
    {
        void OnAfterAnalysis(CallGraph callGraph);

        bool TryGetRoutineSpecializedParameters(SourceRoutineSymbol routine, out ImmutableArray<TypeSymbol> parameterTypes);
    }
}
