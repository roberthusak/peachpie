using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.Symbols;

namespace Peachpie.CodeAnalysis.Utilities
{
    internal class CompilationCounters
    {
        private readonly PhpCompilation _compilation;

        public CompilationCounters(PhpCompilation compilation)
        {
            _compilation = compilation;
        }

        /// <summary>
        /// Obtain the values passed to the constructor of <see cref="CoreTypes.CompilationCountersAttribute"/>.
        /// </summary>
        public IEnumerable<int> GetAttributeCtorArgs()
        {
            // Total number of routines
            yield return _compilation.SourceSymbolCollection.AllRoutines.Count();

            // Number of global functions
            yield return _compilation.SourceSymbolCollection.GetFunctions().Count();

            // Number of specializations
            yield return
                _compilation.SourceSymbolCollection.AllRoutines
                    .SelectMany(r => r.SpecializedOverloads)
                    .Count();
        }
    }
}
