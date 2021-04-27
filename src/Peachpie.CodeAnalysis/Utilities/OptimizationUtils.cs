using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using Devsense.PHP.Syntax;
using Devsense.PHP.Syntax.Ast;
using Microsoft.CodeAnalysis;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.CodeGen;
using Pchp.CodeAnalysis.FlowAnalysis;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Symbols;

#nullable enable

namespace Peachpie.CodeAnalysis.Utilities
{
    internal static class OptimizationUtils
    {
        public static bool CanUsePhpDocTypes(this SourceRoutineSymbol routine) =>
            routine.DeclaringCompilation.Options.ExperimentalOptimization != ExperimentalOptimization.PhpDocForceOnlyFunctions
            || routine is SourceFunctionSymbol;

        public static bool TryGetTypeFromPhpDoc(this SourceParameterSymbol parameter, out TypeSymbol? type)
        {
            if (parameter.PHPDoc != null && parameter.PHPDoc.TypeNamesArray.Length != 0)
            {
                return TryGetSingleTypeFromPhpDoc(parameter, parameter.PHPDoc.TypeNamesArray, out type);
            }

            type = null;
            return false;
        }

        public static bool TryGetTypesFromPhpDoc(this SourceParameterSymbol parameter, out SortedSet<TypeSymbol>? types)
        {
            if (parameter.PHPDoc != null && parameter.PHPDoc.TypeNamesArray.Length != 0)
            {
                types = new SortedSet<TypeSymbol>(SpecializationUtils.ParameterTypeComparer);
                foreach (var typeName in parameter.PHPDoc.TypeNamesArray)
                {
                    if (TryGetSingleTypeFromPhpDoc(parameter, new[] {typeName}, out var type))
                    {
                        types.Add(type!);
                    }
                }

                return types.Count > 0;
            }

            types = null;
            return false;
        }

        private static bool TryGetSingleTypeFromPhpDoc(SourceParameterSymbol parameter, string[] typeNames, out TypeSymbol? type)
        {
            var typectx = parameter.Routine.TypeRefContext;
            var tmask = PHPDoc.GetTypeMask(typectx, typeNames, parameter.Routine.GetNamingContext());

            // Add null as a type if it is specified as a default value
            if (parameter.Initializer?.ConstantValue.IsNull() == true)
            {
                tmask |= typectx.GetNullTypeMask();
            }

            if (!tmask.IsVoid && !tmask.IsAnyType)
            {
                type = parameter.DeclaringCompilation.GetTypeFromTypeRef(typectx, tmask);
                return true;
            }

            type = null;
            return false;
        }

        public static bool TryExtractOriginalFromSpecializedOverloads(IList<MethodSymbol> overloads, out SourceRoutineSymbol? orig)
        {
            var origCandidate =
                overloads
                    .OfType<SourceRoutineSymbol>()
                    .FirstOrDefault(overload => overload.SpecializedOverloads.Length > 0);

            if (origCandidate == null || overloads.Count < 2)       // A single method can be considered ambiguous if its declaration is conditional (we are not interested in that case)
            {
                orig = null;
                return false;
            }

            bool allAreSpecOverloads =
                overloads
                    .All(overload =>
                        overload == origCandidate || origCandidate.SpecializedOverloads.Contains(overload));

            if (allAreSpecOverloads)
            {
                orig = origCandidate;
                return true;
            }
            else
            {
                orig = null;
                return false;
            }
        }

        public static MethodSymbol TryReduceOverloadAmbiguity(
            this AmbiguousMethodSymbol ambiguousRoutine,
            ExperimentalOptimization optimization,
            TypeRefContext typeRefContext,
            ImmutableArray<BoundArgument> arguments = default)
        {
            if (!TryExtractOriginalFromSpecializedOverloads(ambiguousRoutine.Ambiguities, out var origOverload))
            {
                // This ambiguity is not caused by specialized overloads
                return ambiguousRoutine;
            }

            if (arguments.IsDefault || arguments.Any(a => a.IsUnpacking) || origOverload!.SourceParameters.Any(p => p.Syntax.PassedByRef))
            {
                // TODO: Statically compile a disambiguation routine and use it in general places like this

                // If judging only by name or static resolution is not supported, return only the original overload if we want the conservative approach
                return
                    (optimization.HasStaticCallSites() || optimization.HasBranchedCallSites())
                    ? (MethodSymbol)origOverload!
                    : ambiguousRoutine;
            }

            var specializedOverloads =
                ambiguousRoutine.Ambiguities
                .Where(a => a != origOverload)
                .OfType<SourceRoutineSymbol>()
                .ToImmutableArray();

            var resultOverloadsBuilder = specializedOverloads.ToBuilder();

            foreach (var specializedOverload in specializedOverloads)
            {
                Debug.Assert(specializedOverload.SourceParameters.Length == origOverload!.SourceParameters.Length);

                bool allAlways = true;
                bool anyNever = false;
                for (int i = 0; i < specializedOverload.SourceParameters.Length; i++)
                {
                    var param = specializedOverload.SourceParameters[i];

                    BoundExpression argValue;
                    TypeRefContext valueTypeRefContext;
                    if (i < arguments.Length)
                    {
                        argValue = arguments[i].Value;
                        valueTypeRefContext = typeRefContext;
                    }
                    else
                    {
                        if (param.Initializer == null)
                        {
                            allAlways = false;
                            anyNever = true;
                            break;
                        }
                        else
                        {
                            argValue = param.Initializer;
                            valueTypeRefContext = specializedOverload.TypeRefContext;
                        }
                    }

                    if (origOverload.SourceParameters[i].Type != specializedOverload.SourceParameters[i].Type)
                    {
                        var specInfo = SpecializationUtils.GetInfo(ambiguousRoutine.Ambiguities[0].DeclaringCompilation, valueTypeRefContext, argValue, param.Type);
                        allAlways &= specInfo.Kind == SpecializationKind.Always;
                        anyNever |= specInfo.Kind == SpecializationKind.Never;
                    }
                }

                if (allAlways)
                {
                    // Return the first overload perfectly matching the types (they are supposed to be ordered by their optimization level)
                    return specializedOverload;
                }
                else if (anyNever)
                {
                    // Argument types can never match the parameter types in this overload, remove it from consideration
                    resultOverloadsBuilder.Remove(specializedOverload);
                }
            }

            if (resultOverloadsBuilder.Count == 0 || optimization.HasStaticCallSites())
            {
                // We removed all the specialized overloads out of the options or we are not allowed to allow more than one overload
                return origOverload!;
            }
            else if (resultOverloadsBuilder.Count == specializedOverloads.Length)
            {
                // We did not reduce anything, return the original ambiguous method symbol
                return ambiguousRoutine;
            }
            else
            {
                // We removed some of the overloads, recreate the ambiguous method symbol with the smaller set
                resultOverloadsBuilder.Add(origOverload!);
                return new AmbiguousMethodSymbol(ImmutableArray<MethodSymbol>.CastUp(resultOverloadsBuilder.ToImmutable()), true);
            }
        }

        public static void EmitRuntimeCounterMark(CodeGenerator cg, CoreMethod incrementMethod)
        {
            Debug.Assert(incrementMethod.DeclaringClass == cg.CoreTypes.RuntimeCounters.Symbol);

            var ret = cg.EmitCall(ILOpCode.Call, incrementMethod.Symbol);

            Debug.Assert(ret.IsVoid());
        }
    }
}
