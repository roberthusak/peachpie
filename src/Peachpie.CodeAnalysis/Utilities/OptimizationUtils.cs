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
        internal static SourceFunctionSymbol? TryCreatePhpDocOverload(this SourceFunctionSymbol routine)
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

        internal static bool TryGetTypeFromPhpDoc(this SourceParameterSymbol parameter, out TypeSymbol? type)
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
