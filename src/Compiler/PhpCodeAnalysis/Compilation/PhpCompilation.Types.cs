﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Pchp.CodeAnalysis.FlowAnalysis;
using Pchp.CodeAnalysis.Symbols;
using System.Diagnostics;
using Pchp.Core;
using Pchp.CodeAnalysis.Semantics.Model;
using Microsoft.CodeAnalysis.RuntimeMembers;
using Roslyn.Utilities;
using System.Collections.Immutable;
using System.Threading;

namespace Pchp.CodeAnalysis
{
    partial class PhpCompilation
    {
        private readonly WellKnownMembersSignatureComparer _wellKnownMemberSignatureComparer;

        /// <summary>
        /// An array of cached well known types available for use in this Compilation.
        /// Lazily filled by GetWellKnownType method.
        /// </summary>
        private NamedTypeSymbol[] _lazyWellKnownTypes;

        /// <summary>
        /// Lazy cache of well known members.
        /// Not yet known value is represented by ErrorTypeSymbol.UnknownResultType
        /// </summary>
        private Symbol[] _lazyWellKnownTypeMembers;

        #region CoreTypes, CoreMethods

        /// <summary>
        /// Well known types associated with this compilation.
        /// </summary>
        public CoreTypes CoreTypes => _coreTypes;
        readonly CoreTypes _coreTypes;

        /// <summary>
        /// Well known methods associated with this compilation.
        /// </summary>
        public CoreMethods CoreMethods => _coreMethods;
        readonly CoreMethods _coreMethods;

        #endregion

        #region PHP Type Hierarchy

        GlobalSemantics _model;

        /// <summary>
        /// Gets global semantics. To be replaced once we implement SyntaxNode (<see cref="CommonGetSemanticModel"/>).
        /// </summary>
        internal GlobalSemantics GlobalSemantics => _model ?? (_model = new GlobalSemantics(this));

        /// <summary>
        /// Merges two CLR types into one, according to PCHP type hierarchy.
        /// </summary>
        /// <param name="first">First type.</param>
        /// <param name="second">Second type.</param>
        /// <returns>One type convering both <paramref name="first"/> and <paramref name="second"/> types.</returns>
        public TypeSymbol Merge(TypeSymbol first, TypeSymbol second)
        {
            Contract.ThrowIfNull(first);
            Contract.ThrowIfNull(second);

            Debug.Assert(first != CoreTypes.PhpAlias && second != CoreTypes.PhpAlias);

            // merge is not needed:
            if (first == second || first == CoreTypes.PhpValue)
                return first;

            if (second == CoreTypes.PhpValue)
                return second;

            // a number (int | double)
            if (IsNumber(first) && IsNumber(second))
                return CoreTypes.PhpNumber;

            // a string types unification
            if (IsAString(first) && IsAString(second))
                return CoreTypes.PhpString; // a string builder; if both are system.string, system.string is returned earlier

            // TODO: simple array & PhpArray => PhpArray

            if (first.IsOfType(second)) return second;  // A >> B -> B
            if (second.IsOfType(first)) return first;   // A << B -> A

            if (!IsAString(first) && !IsAString(second) &&
                !first.IsOfType(CoreTypes.IPhpArray) && !second.IsOfType(CoreTypes.IPhpArray))
            {
                // unify class types to the common one (lowest)
                if (first.IsReferenceType && second.IsReferenceType)
                {
                    // TODO: find common base
                    // TODO: otherwise find a common interface

                    return CoreTypes.Object;
                }
            }

            if (first.IsOfType(CoreTypes.IPhpArray) && second.IsOfType(CoreTypes.IPhpArray))
                return CoreTypes.IPhpArray;
            
            // most common PHP value type
            return CoreTypes.PhpValue;
        }

        /// <summary>
        /// Determines whether given type is treated as a PHP number (<c>int</c> or <c>double</c>).
        /// </summary>
        public bool IsNumber(TypeSymbol type)
        {
            Contract.ThrowIfNull(type);

            return
                type.SpecialType == SpecialType.System_Double ||
                type.SpecialType == SpecialType.System_Int32 ||
                type.SpecialType == SpecialType.System_Int64 ||
                type == CoreTypes.PhpNumber;
        }

        /// <summary>
        /// Determines given type is treated as a string (UTF16 string or PHP string builder).
        /// </summary>
        public bool IsAString(TypeSymbol type)
        {
            Contract.ThrowIfNull(type);

            return
                type.SpecialType == SpecialType.System_String ||
                type == CoreTypes.PhpString;
        }

        #endregion

        /// <summary>
        /// Lookup member declaration in well known type used by this Compilation.
        /// </summary>
        /// <remarks>
        /// If a well-known member of a generic type instantiation is needed use this method to get the corresponding generic definition and 
        /// <see cref="MethodSymbol.AsMember"/> to construct an instantiation.
        /// </remarks>
        internal Symbol GetWellKnownTypeMember(WellKnownMember member)
        {
            Debug.Assert(member >= 0 && member < WellKnownMember.Count);

            // Test hook: if a member is marked missing, then return null.
            if (IsMemberMissing(member)) return null;

            if (_lazyWellKnownTypeMembers == null || ReferenceEquals(_lazyWellKnownTypeMembers[(int)member], ErrorTypeSymbol.UnknownResultType))
            {
                if (_lazyWellKnownTypeMembers == null)
                {
                    var wellKnownTypeMembers = new Symbol[(int)WellKnownMember.Count];

                    for (int i = 0; i < wellKnownTypeMembers.Length; i++)
                    {
                        wellKnownTypeMembers[i] = ErrorTypeSymbol.UnknownResultType;
                    }

                    Interlocked.CompareExchange(ref _lazyWellKnownTypeMembers, wellKnownTypeMembers, null);
                }

                MemberDescriptor descriptor = WellKnownMembers.GetDescriptor(member);
                NamedTypeSymbol type = descriptor.DeclaringTypeId <= (int)SpecialType.Count
                                            ? this.GetSpecialType((SpecialType)descriptor.DeclaringTypeId)
                                            : this.GetWellKnownType((WellKnownType)descriptor.DeclaringTypeId);
                Symbol result = null;

                if (!type.IsErrorType())
                {
                    result = GetRuntimeMember(type, ref descriptor, _wellKnownMemberSignatureComparer, accessWithinOpt: this.Assembly);
                }

                Interlocked.CompareExchange(ref _lazyWellKnownTypeMembers[(int)member], result, ErrorTypeSymbol.UnknownResultType);
            }

            return _lazyWellKnownTypeMembers[(int)member];
        }

        internal NamedTypeSymbol GetWellKnownType(WellKnownType type)
        {
            Debug.Assert(type >= WellKnownType.First && type <= WellKnownType.Last);

            int index = (int)type - (int)WellKnownType.First;
            if (_lazyWellKnownTypes == null || (object)_lazyWellKnownTypes[index] == null)
            {
                if (_lazyWellKnownTypes == null)
                {
                    Interlocked.CompareExchange(ref _lazyWellKnownTypes, new NamedTypeSymbol[(int)WellKnownTypes.Count], null);
                }

                string mdName = type.GetMetadataName();
                var warnings = DiagnosticBag.GetInstance();
                NamedTypeSymbol result;

                if (IsTypeMissing(type))
                {
                    result = null;
                }
                else
                {
                    result = this.SourceAssembly.GetTypeByMetadataName(
                        mdName, includeReferences: true, useCLSCompliantNameArityEncoding: true, isWellKnownType: true, warnings: warnings);
                }

                if ((object)result == null)
                {
                    // TODO: should GetTypeByMetadataName rather return a missing symbol?
                    //MetadataTypeName emittedName = MetadataTypeName.FromFullName(mdName, useCLSCompliantNameArityEncoding: true);
                    //result = new MissingMetadataTypeSymbol.TopLevel(this.Assembly.Modules[0], ref emittedName, type);
                    Debug.Assert(false);
                    result = new MissingMetadataTypeSymbol(mdName, 0, false);
                }

                if ((object)Interlocked.CompareExchange(ref _lazyWellKnownTypes[index], result, null) != null)
                {
                    Debug.Assert(
                        result == _lazyWellKnownTypes[index] || (_lazyWellKnownTypes[index].IsErrorType() && result.IsErrorType())
                    );
                }
                else
                {
                    // TODO //AdditionalCodegenWarnings.AddRange(warnings);
                }

                warnings.Free();
            }

            return _lazyWellKnownTypes[index];
        }

        /// <summary>
        /// Get the symbol for the predefined type from the Cor Library referenced by this
        /// compilation.
        /// </summary>
        internal new NamedTypeSymbol GetSpecialType(SpecialType specialType)
        {
            return (NamedTypeSymbol)CommonGetSpecialType(specialType);
        }

        protected override INamedTypeSymbol CommonGetSpecialType(SpecialType specialType)
        {
            return this.CorLibrary.GetSpecialType(specialType);
        }

        /// <summary>
        /// Get the symbol for the predefined type member from the COR Library referenced by this compilation.
        /// </summary>
        internal Symbol GetSpecialTypeMember(SpecialMember specialMember)
        {
            return this.CorLibrary.GetDeclaredSpecialTypeMember(specialMember);
        }

        internal static Symbol GetRuntimeMember(NamedTypeSymbol declaringType, ref MemberDescriptor descriptor, SignatureComparer<MethodSymbol, FieldSymbol, PropertySymbol, TypeSymbol, ParameterSymbol> comparer, IAssemblySymbol accessWithinOpt)
        {
            Symbol result = null;
            SymbolKind targetSymbolKind;
            MethodKind targetMethodKind = MethodKind.Ordinary;
            bool isStatic = (descriptor.Flags & MemberFlags.Static) != 0;

            switch (descriptor.Flags & MemberFlags.KindMask)
            {
                case MemberFlags.Constructor:
                    targetSymbolKind = SymbolKind.Method;
                    targetMethodKind = MethodKind.Constructor;
                    //  static constructors are never called explicitly
                    Debug.Assert(!isStatic);
                    break;

                case MemberFlags.Method:
                    targetSymbolKind = SymbolKind.Method;
                    break;

                case MemberFlags.PropertyGet:
                    targetSymbolKind = SymbolKind.Method;
                    targetMethodKind = MethodKind.PropertyGet;
                    break;

                case MemberFlags.Field:
                    targetSymbolKind = SymbolKind.Field;
                    break;

                case MemberFlags.Property:
                    targetSymbolKind = SymbolKind.Property;
                    break;

                default:
                    throw ExceptionUtilities.UnexpectedValue(descriptor.Flags);
            }

            foreach (var member in declaringType.GetMembers(descriptor.Name))
            {
                Debug.Assert(member.Name.Equals(descriptor.Name));

                if (member.Kind != targetSymbolKind || member.IsStatic != isStatic || !(member.DeclaredAccessibility == Accessibility.Public))
                {
                    continue;
                }

                switch (targetSymbolKind)
                {
                    case SymbolKind.Method:
                        {
                            MethodSymbol method = (MethodSymbol)member;
                            MethodKind methodKind = method.MethodKind;
                            // Treat user-defined conversions and operators as ordinary methods for the purpose
                            // of matching them here.
                            if (methodKind == MethodKind.Conversion || methodKind == MethodKind.UserDefinedOperator)
                            {
                                methodKind = MethodKind.Ordinary;
                            }

                            if (method.Arity != descriptor.Arity || methodKind != targetMethodKind ||
                                ((descriptor.Flags & MemberFlags.Virtual) != 0) != (method.IsVirtual || method.IsOverride || method.IsAbstract))
                            {
                                continue;
                            }

                            if (!comparer.MatchMethodSignature(method, descriptor.Signature))
                            {
                                continue;
                            }
                        }

                        break;

                    case SymbolKind.Property:
                        {
                            PropertySymbol property = (PropertySymbol)member;
                            if (((descriptor.Flags & MemberFlags.Virtual) != 0) != (property.IsVirtual || property.IsOverride || property.IsAbstract))
                            {
                                continue;
                            }

                            if (!comparer.MatchPropertySignature(property, descriptor.Signature))
                            {
                                continue;
                            }
                        }

                        break;

                    case SymbolKind.Field:
                        if (!comparer.MatchFieldSignature((FieldSymbol)member, descriptor.Signature))
                        {
                            continue;
                        }

                        break;

                    default:
                        throw ExceptionUtilities.UnexpectedValue(targetSymbolKind);
                }

                // ambiguity
                if ((object)result != null)
                {
                    result = null;
                    break;
                }

                result = member;
            }
            return result;
        }

        internal IEnumerable<IAssemblySymbol> ProbingAssemblies
        {
            get
            {
                foreach (var pair in CommonGetBoundReferenceManager().GetReferencedAssemblies())
                    yield return pair.Value;

                yield return this.SourceAssembly;
            }
        }

        protected override INamedTypeSymbol CommonGetTypeByMetadataName(string metadataName)
        {
            return ProbingAssemblies
                    .Select(a => a.GetTypeByMetadataName(metadataName))
                    .Where(a => a != null)
                    .FirstOrDefault();
        }

        /// <summary>
        /// Resolves <see cref="TypeSymbol"/> best fitting given type mask.
        /// </summary>
        internal NamedTypeSymbol GetTypeFromTypeRef(TypeRefContext typeCtx, TypeRefMask typeMask)
        {
            if (typeMask.IsRef)
            {
                return CoreTypes.PhpAlias;
            }

            if (!typeMask.IsAnyType)
            {
                if (typeMask.IsVoid)
                {
                    return CoreTypes.Void;
                }

                var types = typeCtx.GetTypes(typeMask);
                Debug.Assert(types.Count != 0);

                // determine best fitting CLR type based on defined PHP types hierarchy
                var result = GetTypeFromTypeRef(types[0]);

                for (int i = 1; i < types.Count; i++)
                {
                    var tdesc = GetTypeFromTypeRef(types[i]);
                    result = (NamedTypeSymbol)Merge(result, GetTypeFromTypeRef(types[i]));
                }

                //
                return result;
            }

            // most common type
            return CoreTypes.PhpValue;
        }

        internal NamedTypeSymbol GetTypeFromTypeRef(ITypeRef t)
        {
            if (t is PrimitiveTypeRef)
            {
                return GetTypeFromTypeRef((PrimitiveTypeRef)t);
            }
            else if (t is ClassTypeRef)
            {
                return (NamedTypeSymbol)GlobalSemantics.GetType(t.QualifiedName) ?? CoreTypes.Object.Symbol;
            }
            else if (t is ArrayTypeRef)
            {
                return this.CoreTypes.IPhpArray;
            }
            else if (t is LambdaTypeRef)
            {

            }

            throw new ArgumentException();
        }

        NamedTypeSymbol GetTypeFromTypeRef(PrimitiveTypeRef t)
        {
            switch (t.TypeCode)
            {
                case PhpTypeCode.Double: return CoreTypes.Double;
                case PhpTypeCode.Long: return CoreTypes.Long;
                case PhpTypeCode.Int32: return CoreTypes.Int32;
                case PhpTypeCode.Boolean: return CoreTypes.Boolean;
                case PhpTypeCode.String: return CoreTypes.String;
                case PhpTypeCode.WritableString: return CoreTypes.PhpString;
                case PhpTypeCode.PhpArray: return CoreTypes.IPhpArray;
                case PhpTypeCode.Callable: return CoreTypes.IPhpCallable;
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Resolves <see cref="INamedTypeSymbol"/> best fitting given type mask.
        /// </summary>
        internal NamedTypeSymbol GetTypeFromTypeRef(SourceRoutineSymbol routine, TypeRefMask typeMask)
        {
            var fctx = routine.FlowContext;
            if (fctx != null)
            {
                return this.GetTypeFromTypeRef(fctx.TypeRefContext, typeMask);
            }

            throw new InvalidOperationException();
        }

        /// <summary>
        /// Used to generate the dynamic attributes for the required typesymbol.
        /// </summary>
        internal static class DynamicTransformsEncoder
        {
            internal static ImmutableArray<TypedConstant> Encode(TypeSymbol type, TypeSymbol booleanType, int customModifiersCount, RefKind refKind)
            {
                var flagsBuilder = ArrayBuilder<bool>.GetInstance();
                EncodeInternal(type, customModifiersCount, refKind, flagsBuilder);
                Debug.Assert(flagsBuilder.Any());
                Debug.Assert(flagsBuilder.Contains(true));

                var constantsBuilder = ArrayBuilder<TypedConstant>.GetInstance(flagsBuilder.Count);
                foreach (bool flag in flagsBuilder)
                {
                    constantsBuilder.Add(new TypedConstant(booleanType, TypedConstantKind.Primitive, flag));
                }

                flagsBuilder.Free();
                return constantsBuilder.ToImmutableAndFree();
            }

            internal static ImmutableArray<bool> Encode(TypeSymbol type, int customModifiersCount, RefKind refKind)
            {
                var transformFlagsBuilder = ArrayBuilder<bool>.GetInstance();
                EncodeInternal(type, customModifiersCount, refKind, transformFlagsBuilder);
                return transformFlagsBuilder.ToImmutableAndFree();
            }

            internal static void EncodeInternal(TypeSymbol type, int customModifiersCount, RefKind refKind, ArrayBuilder<bool> transformFlagsBuilder)
            {
                Debug.Assert(!transformFlagsBuilder.Any());

                if (refKind != RefKind.None)
                {
                    // Native compiler encodes an extra transform flag, always false, for ref/out parameters.
                    transformFlagsBuilder.Add(false);
                }

                // Native compiler encodes an extra transform flag, always false, for each custom modifier.
                HandleCustomModifiers(customModifiersCount, transformFlagsBuilder);

                type.VisitType(s_encodeDynamicTransform, transformFlagsBuilder);
            }

            private static readonly Func<TypeSymbol, ArrayBuilder<bool>, bool, bool> s_encodeDynamicTransform = (type, transformFlagsBuilder, isNestedNamedType) =>
            {
                // Encode transforms flag for this type and it's custom modifiers (if any).
                switch (type.TypeKind)
                {
                    case TypeKind.Dynamic:
                        transformFlagsBuilder.Add(true);
                        break;

                    case TypeKind.Array:
                        HandleCustomModifiers(((ArrayTypeSymbol)type).CustomModifiers.Length, transformFlagsBuilder);
                        transformFlagsBuilder.Add(false);
                        break;

                    case TypeKind.Pointer:
                        //HandleCustomModifiers(((PointerTypeSymbol)type).CustomModifiers.Length, transformFlagsBuilder);
                        //transformFlagsBuilder.Add(false);
                        //break;
                        throw new NotImplementedException();

                    default:
                        // Encode transforms flag for this type.
                        // For nested named types, a single flag (false) is encoded for the entire type name, followed by flags for all of the type arguments.
                        // For example, for type "A<T>.B<dynamic>", encoded transform flags are:
                        //      {
                        //          false,  // Type "A.B"
                        //          false,  // Type parameter "T"
                        //          true,   // Type parameter "dynamic"
                        //      }

                        if (!isNestedNamedType)
                        {
                            transformFlagsBuilder.Add(false);
                        }
                        break;
                }

                // Continue walking types
                return false;
            };

            private static void HandleCustomModifiers(int customModifiersCount, ArrayBuilder<bool> transformFlagsBuilder)
            {
                for (int i = 0; i < customModifiersCount; i++)
                {
                    // Native compiler encodes an extra transforms flag, always false, for each custom modifier.
                    transformFlagsBuilder.Add(false);
                }
            }
        }

        internal class SpecialMembersSignatureComparer : SignatureComparer<MethodSymbol, FieldSymbol, PropertySymbol, TypeSymbol, ParameterSymbol>
        {
            // Fields
            public static readonly SpecialMembersSignatureComparer Instance = new SpecialMembersSignatureComparer();

            // Methods
            protected SpecialMembersSignatureComparer()
            {
            }

            protected override TypeSymbol GetMDArrayElementType(TypeSymbol type)
            {
                if (type.Kind != SymbolKind.ArrayType)
                {
                    return null;
                }
                ArrayTypeSymbol array = (ArrayTypeSymbol)type;
                if (array.IsSZArray)
                {
                    return null;
                }
                return array.ElementType;
            }

            protected override TypeSymbol GetFieldType(FieldSymbol field)
            {
                return field.Type;
            }

            protected override TypeSymbol GetPropertyType(PropertySymbol property)
            {
                return property.Type;
            }

            protected override TypeSymbol GetGenericTypeArgument(TypeSymbol type, int argumentIndex)
            {
                if (type.Kind != SymbolKind.NamedType)
                {
                    return null;
                }
                NamedTypeSymbol named = (NamedTypeSymbol)type;
                if (named.Arity <= argumentIndex)
                {
                    return null;
                }
                if ((object)named.ContainingType != null)
                {
                    return null;
                }
                return named.TypeArguments[argumentIndex];//return named.TypeArgumentsNoUseSiteDiagnostics[argumentIndex];
            }

            protected override TypeSymbol GetGenericTypeDefinition(TypeSymbol type)
            {
                if (type.Kind != SymbolKind.NamedType)
                {
                    return null;
                }
                NamedTypeSymbol named = (NamedTypeSymbol)type;
                if ((object)named.ContainingType != null)
                {
                    return null;
                }
                if (named.Arity == 0)
                {
                    return null;
                }
                return (NamedTypeSymbol)named.OriginalDefinition;
            }

            protected override ImmutableArray<ParameterSymbol> GetParameters(MethodSymbol method)
            {
                return method.Parameters;
            }

            protected override ImmutableArray<ParameterSymbol> GetParameters(PropertySymbol property)
            {
                return property.Parameters;
            }

            protected override TypeSymbol GetParamType(ParameterSymbol parameter)
            {
                return parameter.Type;
            }

            protected override TypeSymbol GetPointedToType(TypeSymbol type)
            {
                if (type.Kind == SymbolKind.PointerType)
                    throw new NotImplementedException();

                return null;
            }

            protected override TypeSymbol GetReturnType(MethodSymbol method)
            {
                return method.ReturnType;
            }

            protected override TypeSymbol GetSZArrayElementType(TypeSymbol type)
            {
                if (type.Kind != SymbolKind.ArrayType)
                {
                    return null;
                }
                ArrayTypeSymbol array = (ArrayTypeSymbol)type;
                if (!array.IsSZArray)
                {
                    return null;
                }
                return array.ElementType;
            }

            protected override bool IsByRefParam(ParameterSymbol parameter)
            {
                return parameter.RefKind != RefKind.None;
            }

            protected override bool IsGenericMethodTypeParam(TypeSymbol type, int paramPosition)
            {
                if (type.Kind != SymbolKind.TypeParameter)
                {
                    return false;
                }
                TypeParameterSymbol typeParam = (TypeParameterSymbol)type;
                if (typeParam.ContainingSymbol.Kind != SymbolKind.Method)
                {
                    return false;
                }
                return (typeParam.Ordinal == paramPosition);
            }

            protected override bool IsGenericTypeParam(TypeSymbol type, int paramPosition)
            {
                if (type.Kind != SymbolKind.TypeParameter)
                {
                    return false;
                }
                TypeParameterSymbol typeParam = (TypeParameterSymbol)type;
                if (typeParam.ContainingSymbol.Kind != SymbolKind.NamedType)
                {
                    return false;
                }
                return (typeParam.Ordinal == paramPosition);
            }

            protected override bool MatchArrayRank(TypeSymbol type, int countOfDimensions)
            {
                if (type.Kind != SymbolKind.ArrayType)
                {
                    return false;
                }

                ArrayTypeSymbol array = (ArrayTypeSymbol)type;
                return (array.Rank == countOfDimensions);
            }

            protected override bool MatchTypeToTypeId(TypeSymbol type, int typeId)
            {
                return (int)type.SpecialType == typeId;
            }
        }

        private class WellKnownMembersSignatureComparer : SpecialMembersSignatureComparer
        {
            private readonly PhpCompilation _compilation;

            public WellKnownMembersSignatureComparer(PhpCompilation compilation)
            {
                _compilation = compilation;
            }

            protected override bool MatchTypeToTypeId(TypeSymbol type, int typeId)
            {
                WellKnownType wellKnownId = (WellKnownType)typeId;
                if (wellKnownId >= WellKnownType.First && wellKnownId <= WellKnownType.Last)
                {
                    return (type == _compilation.GetWellKnownType(wellKnownId));
                }

                return base.MatchTypeToTypeId(type, typeId);
            }
        }
    }
}
