using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Pchp.CodeAnalysis.FlowAnalysis.Souffle
{
    internal class SouffleType
    {
        public const string SymbolTypeName = "symbol";

        public string Name { get; }

        public ImmutableArray<string> ChildTypes { get; }

        public SouffleType(string name, ImmutableArray<string> childTypes)
        {
            Name = name;
            ChildTypes = childTypes;
        }

        public string GetDeclaration()
        {
            if (ChildTypes.Length == 0)
            {
                return $".type {Name} <: {SymbolTypeName}";
            }
            else
            {
                return $".type {Name} = {string.Join(" | ", ChildTypes)}";
            }
        }
    }
}
