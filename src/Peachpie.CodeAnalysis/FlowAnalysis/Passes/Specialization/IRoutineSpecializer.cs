using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Pchp.CodeAnalysis.Symbols;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    internal interface IRoutineSpecializer
    {
        void OnAfterAnalysis();

        bool TryGetRoutineSpecializedParameters(SourceRoutineSymbol routine, out ImmutableArray<TypeSymbol> parameterTypes);
    }
}
