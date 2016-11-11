﻿using Microsoft.CodeAnalysis;
using Pchp.CodeAnalysis.FlowAnalysis;
using Pchp.CodeAnalysis.Symbols;
using Pchp.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Devsense.PHP.Syntax;

namespace Pchp.CodeAnalysis
{
    internal static partial class TypeRefFactory
    {
        #region Primitive Types

        internal static readonly PrimitiveTypeRef/*!*/BoolTypeRef = new PrimitiveTypeRef(PhpTypeCode.Boolean);
        internal static readonly PrimitiveTypeRef/*!*/LongTypeRef = new PrimitiveTypeRef(PhpTypeCode.Long);
        internal static readonly PrimitiveTypeRef/*!*/DoubleTypeRef = new PrimitiveTypeRef(PhpTypeCode.Double);
        internal static readonly PrimitiveTypeRef/*!*/StringTypeRef = new PrimitiveTypeRef(PhpTypeCode.String);
        internal static readonly PrimitiveTypeRef/*!*/WritableStringRef = new PrimitiveTypeRef(PhpTypeCode.WritableString);
        internal static readonly PrimitiveTypeRef/*!*/ArrayTypeRef = new PrimitiveTypeRef(PhpTypeCode.PhpArray);
        internal static readonly PrimitiveTypeRef/*!*/CallableTypeRef = new PrimitiveTypeRef(PhpTypeCode.Callable);

        #endregion

        /// <summary>
        /// Converts CLR type symbol to TypeRef used by flow analysis.
        /// </summary>
        public static ITypeRef Create(TypeSymbol t)
        {
            Contract.ThrowIfNull(t);

            switch (t.SpecialType)
            {
                case SpecialType.System_Void: throw new ArgumentException();
                case SpecialType.System_Int32:
                case SpecialType.System_Int64: return LongTypeRef;
                case SpecialType.System_String: return StringTypeRef;
                case SpecialType.System_Double: return DoubleTypeRef;
                case SpecialType.System_Boolean: return BoolTypeRef;
                case SpecialType.System_Object: return new ClassTypeRef(NameUtils.SpecialNames.System_Object);
                case SpecialType.System_DateTime: return new ClassTypeRef(new QualifiedName(new Name("DateTime"), new[] { new Name("System") }));
                default:
                    return new ClassTypeRef(((NamedTypeSymbol)t).MakeQualifiedName());
            }
        }

        public static ITypeRef Create(ConstantValue c)
        {
            Contract.ThrowIfNull(c);

            switch (c.SpecialType)
            {
                case SpecialType.System_Int32:
                case SpecialType.System_Int64: return LongTypeRef;
                case SpecialType.System_String: return StringTypeRef;
                case SpecialType.System_Double: return DoubleTypeRef;
                case SpecialType.System_Boolean: return BoolTypeRef;
                default:
                    throw new NotImplementedException();
            }
        }

        public static TypeRefMask CreateMask(TypeRefContext ctx, TypeSymbol t)
        {
            Contract.ThrowIfNull(t);

            switch (t.SpecialType)
            {
                case SpecialType.System_Void: return 0;
                case SpecialType.System_Int64: return ctx.GetLongTypeMask();
                case SpecialType.System_String: return ctx.GetStringTypeMask();
                case SpecialType.System_Double: return ctx.GetDoubleTypeMask();
                case SpecialType.System_Boolean: return ctx.GetBooleanTypeMask();
                case SpecialType.None:
                    if (t.ContainingAssembly.IsPchpCorLibrary)
                    {
                        if (t.Name == "PhpValue") return TypeRefMask.AnyType;
                        if (t.Name == "PhpAlias") return TypeRefMask.AnyType.WithRefFlag;
                        if (t.Name == "PhpNumber") return ctx.GetNumberTypeMask();
                        if (t.Name == "PhpString") return ctx.GetWritableStringTypeMask();
                        if (t.Name == "PhpArray") return ctx.GetArrayTypeMask();
                        if (t.Name == "IPhpCallable") return ctx.GetCallableTypeMask();
                    }

                    break;
            }

            return CreateMask(ctx, Create(t));
        }

        public static TypeRefMask CreateMask(TypeRefContext ctx, ITypeRef tref)
        {
            Contract.ThrowIfNull(tref);

            TypeRefMask result = 0;

            result.AddType(ctx.AddToContext(tref));

            if (!tref.IsPrimitiveType && !tref.IsArray)
            {
                result.IncludesSubclasses = true;
            }

            return result;
        }

        /// <summary>
        /// Creates type context for a method within given type, determines naming, type context.
        /// </summary>
        public static TypeRefContext/*!*/CreateTypeRefContext(SourceTypeSymbol/*!*/containingType)
        {
            Contract.ThrowIfNull(containingType);

            var typeDecl = containingType.Syntax;
            return new TypeRefContext(typeDecl.ContainingSourceUnit, containingType);
        }
    }
}
