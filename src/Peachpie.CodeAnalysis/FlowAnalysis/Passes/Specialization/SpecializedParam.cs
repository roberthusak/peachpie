using System;
using System.Collections.Generic;
using System.Text;
using Devsense.PHP.Syntax.Ast;
using Pchp.CodeAnalysis.Symbols;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization
{
    [Flags]
    internal enum SpecializationFlags
    {
        None = 0,
        IsNull = 1
    }

    internal readonly struct SpecializedParam
    {
        public TypeSymbol Type { get; }

        public SpecializationFlags Flags { get; }

        public SpecializedParam(TypeSymbol type, SpecializationFlags flags = SpecializationFlags.None)
        {
            Type = type;
            Flags = flags;
        }

        public static implicit operator SpecializedParam(TypeSymbol type) => new SpecializedParam(type);

        public static implicit operator TypeSymbol(SpecializedParam param) => param.Type;
    }
}
