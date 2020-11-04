using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Devsense.PHP.Syntax;
using Devsense.PHP.Syntax.Ast;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.FlowAnalysis;
using Pchp.CodeAnalysis.Symbols;

#nullable enable

namespace Peachpie.CodeAnalysis.Utilities
{
    internal static class OptimizationUtils
    {
        public static SourceFunctionSymbol? TryCreatePhpDocOverload(this SourceFunctionSymbol routine)
        {
            if (routine.SourceParameters.Any(CanBeParameterSpecialized))
            {
                var overload = new SourceFunctionSymbol(routine.ContainingFile, (FunctionDecl)routine.Syntax)
                {
                    IsSpecializedOverload = true
                };

                routine.SpecializedOverloads = new[] { (SourceRoutineSymbol)overload }.ToImmutableArray();

                return overload;
            }
            else
            {
                return null;
            }
        }

        public static bool TryGetTypeFromPhpDoc(this SourceParameterSymbol parameter, out TypeSymbol? type)
        {
            if (parameter.PHPDocOpt != null && parameter.PHPDocOpt.TypeNamesArray.Length != 0)
            {
                var typectx = parameter.Routine.TypeRefContext;
                var tmask = PHPDoc.GetTypeMask(typectx, parameter.PHPDocOpt.TypeNamesArray, parameter.Routine.GetNamingContext());
                if (!tmask.IsVoid && !tmask.IsAnyType)
                {
                    type = parameter.DeclaringCompilation.GetTypeFromTypeRef(typectx, tmask);
                    return true;
                }
            }

            type = null;
            return false;
        }

        public static MethodSymbol TryReduceOverloadAmbiguity(this AmbiguousMethodSymbol ambiguousRoutine, ExperimentalOptimization optimization)
        {
            // If unable to statically determine better specialized overload where we aim at static specialization resolving,
            // resort to the original (general) definition

            // TODO: Generalize for multiple overloads and orders (now it is strongly coupled with SourceSymbolProvider.ResolveFunction)
            var ambiguities = ambiguousRoutine.Ambiguities;
            if (optimization == ExperimentalOptimization.PhpDocOverloadsStatic && ambiguities.Length == 2 &&
                     ambiguities[1] is SourceRoutineSymbol origRoutine && origRoutine.SpecializedOverloads.Length > 0 &&
                     origRoutine.SpecializedOverloads[0] == ambiguities[0])
            {
                return origRoutine;
            }
            else
            {
                return ambiguousRoutine;
            }
        }

        private static bool CanBeParameterSpecialized(SourceParameterSymbol parameter)
        {
            if (parameter.Type.Is_PhpValue() && parameter.TryGetTypeFromPhpDoc(out var type))
            {
                return !type.Is_PhpValue();
            }

            return false;
        }
    }
}
