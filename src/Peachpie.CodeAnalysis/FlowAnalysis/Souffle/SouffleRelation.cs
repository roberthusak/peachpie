using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Pchp.CodeAnalysis.FlowAnalysis.Souffle
{
    internal class SouffleRelation
    {
        public struct Parameter
        {
            public string Name { get; }

            public string Type { get; }

            public Parameter(string name, string type)
            {
                Name = name;
                Type = type;
            }
        }

        public string Name { get; }

        public ImmutableArray<Parameter> Parameters { get; }

        public SouffleRelation(string name, ImmutableArray<Parameter> parameters)
        {
            Name = name;
            Parameters = parameters;
        }
    }
}
