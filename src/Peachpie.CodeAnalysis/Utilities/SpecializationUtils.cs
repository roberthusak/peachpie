using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Text;
using Microsoft.CodeAnalysis;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.CodeGen;
using Pchp.CodeAnalysis.FlowAnalysis;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Symbols;

#nullable enable

// TODO: Move to CodeGen
namespace Peachpie.CodeAnalysis.Utilities
{
    internal enum SpecializationKind
    {
        Never,
        RuntimeDependent,
        Always
    }

    internal delegate TypeRefMask TypeCheckEmitter(CodeGenerator cg, BoundReferenceExpression expr, TypeSymbol to);

    internal struct SpecializationInfo
    {
        public SpecializationInfo(SpecializationKind kind, TypeCheckEmitter? emitter = null)
        {
            this.Kind = kind;
            this.Emitter = emitter;
        }

        public SpecializationKind Kind { get; }

        public TypeCheckEmitter? Emitter { get; }
    }

    internal static class SpecializationUtils
    {
        private static TypeCheckEmitter GetTypeCodeEmitter(int typeCode, Func<TypeRefContext, TypeRefMask, TypeRefMask> maskSelector)
        {
            // NOTE: PhpTypeCode enum in CodeAnalysis and Runtime is different, we use integers here instead

            return (cg, expr, to) =>
            {
                // arg.TypeCode == typeCode

                var place = expr.BindPlace(cg);
                Debug.Assert(place.Type.Is_PhpValue());

                LhsStack lhs = default;
                place.EmitLoadAddress(cg, ref lhs);
                cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.TypeCode.Getter);
                lhs.Dispose();

                cg.Builder.EmitIntConstant(typeCode);
                cg.Builder.EmitOpCode(ILOpCode.Ceq);

                return maskSelector(cg.TypeRefContext, expr.TypeRefMask);
            };
        }

        private static TypeCheckEmitter GetMethodEmitter(Func<CodeGenerator, MethodSymbol> methodSelector, Func<TypeRefContext, TypeRefMask, TypeRefMask> maskSelector)
        {
            return (cg, expr, to) =>
            {
                // arg.Method()

                var place = expr.BindPlace(cg);
                Debug.Assert(place.Type.Is_PhpValue());

                LhsStack lhs = default;
                place.EmitLoadAddress(cg, ref lhs);
                cg.EmitCall(ILOpCode.Call, methodSelector(cg));
                lhs.Dispose();

                return maskSelector(cg.TypeRefContext, expr.TypeRefMask);
            };
        }

        // TODO: Aliases are not handled, the dynamic overloading is skipped instead. Handle them as well.
        private static readonly TypeCheckEmitter PhpValueBoolCheckEmitter = GetTypeCodeEmitter(1, (c, _) => c.GetBooleanTypeMask());
        private static readonly TypeCheckEmitter PhpValueLongCheckEmitter = GetTypeCodeEmitter(2, (c, _) => c.GetLongTypeMask());
        private static readonly TypeCheckEmitter PhpValueDoubleCheckEmitter = GetTypeCodeEmitter(3, (c, _) => c.GetDoubleTypeMask());
        private static readonly TypeCheckEmitter PhpValuePhpArrayCheckEmitter = GetTypeCodeEmitter(4, (c, _) => c.GetArrayTypeMask());
        private static readonly TypeCheckEmitter PhpValueStringCheckEmitter = GetTypeCodeEmitter(5, (c, _) => c.GetStringTypeMask());
        private static readonly TypeCheckEmitter PhpValueObjectCheckEmitter = GetTypeCodeEmitter(7, (c, m) => c.GetObjectsFromMask(m));

        private static readonly TypeCheckEmitter PhpValuePhpStringCheckEmitter = GetMethodEmitter(cg => cg.CoreMethods.PhpValue.IsStringNoAlias, (c, _) => c.GetWritableStringTypeMask());

        public static SpecializationInfo GetInfo(PhpCompilation compilation, TypeRefContext typeCtx, BoundExpression expr, TypeSymbol to)
        {
            // TODO: Try this after passing parameter's TypeRefMask
            //if (expr.TypeRefMask.IsRef || !typeCtx.CanBeSameType(expr.TypeRefMask, toMask))
            //{
            //    return new SpecializationInfo(SpecializationKind.Never);
            //}

            if (expr.TypeRefMask.IsRef)
            {
                // Aliases are currently not supported (their values can be influenced by evaluating other arguments)
                return new SpecializationInfo(SpecializationKind.Never);
            }

            if (to.Is_PhpValue())
            {
                return new SpecializationInfo(SpecializationKind.Always);
            }

            var exprType = expr.Type ?? compilation.GetTypeFromTypeRef(typeCtx, expr.TypeRefMask);
            if (exprType.Is_PhpValue())
            {
                if (to.SpecialType == SpecialType.System_Boolean)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValueBoolCheckEmitter);
                }
                else if (to.SpecialType == SpecialType.System_Int64)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValueLongCheckEmitter);
                }
                else if (to.SpecialType == SpecialType.System_Double)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValueDoubleCheckEmitter);
                }
                else if (to.Is_PhpArray())
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValuePhpArrayCheckEmitter);
                }
                else if (to.SpecialType == SpecialType.System_String)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValueStringCheckEmitter);
                }
                else if (to.Is_PhpString())
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValuePhpStringCheckEmitter);
                }
                else if (to.SpecialType == SpecialType.System_Object)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValueObjectCheckEmitter);
                }

                // TODO: Specific classes etc. (.AsObject() is ...)
            }
            else if (exprType.Is_PhpString() || exprType.SpecialType == SpecialType.System_String)
            {
                // We can always convert between .NET and PHP strings
                if (to.Is_PhpString() || to.SpecialType == SpecialType.System_String)
                {
                    return new SpecializationInfo(SpecializationKind.Always);
                }
            }

            return new SpecializationInfo(SpecializationKind.Never);
        }
    }
}
