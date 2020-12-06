using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Text;
using Microsoft.CodeAnalysis;
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

    internal delegate void TypeCheckEmitter(CodeGenerator cg, BoundReferenceExpression expr, TypeSymbol to);

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
        private static TypeCheckEmitter GetTypeCodeEmitter(int typeCode)
        {
            // NOTE: PhpTypeCode enum in CodeAnalysis and Runtime is different, we use integers here instead

            return (CodeGenerator cg, BoundReferenceExpression expr, TypeSymbol to) =>
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
            };
        }

        // TODO: Aliases are not handled, the dynamic overloading is skipped instead. Handle them as well.
        private readonly static TypeCheckEmitter PhpValueBoolCheckEmitter = GetTypeCodeEmitter(1);
        private readonly static TypeCheckEmitter PhpValueLongCheckEmitter = GetTypeCodeEmitter(2);
        private readonly static TypeCheckEmitter PhpValueDoubleCheckEmitter = GetTypeCodeEmitter(3);
        private readonly static TypeCheckEmitter PhpValuePhpArrayCheckEmitter = GetTypeCodeEmitter(4);
        private readonly static TypeCheckEmitter PhpValueStringCheckEmitter = GetTypeCodeEmitter(5);
        private readonly static TypeCheckEmitter PhpValuePhpStringCheckEmitter = GetTypeCodeEmitter(6);
        private readonly static TypeCheckEmitter PhpValueObjectCheckEmitter = GetTypeCodeEmitter(7);

        public static SpecializationInfo GetInfo(TypeRefContext typeCtx, BoundExpression expr, TypeSymbol to, TypeRefMask toMask)
        {
            // TODO: Try this after passing parameter's TypeRefMask
            //if (expr.TypeRefMask.IsRef || !typeCtx.CanBeSameType(expr.TypeRefMask, toMask))
            //{
            //    return new SpecializationInfo(SpecializationKind.Never);
            //}

            if (to.Is_PhpValue())
            {
                return new SpecializationInfo(SpecializationKind.Always);
            }

            // TODO: Clean this up
            if (expr.Type?.Is_PhpValue() == true || expr.TypeRefMask.IsAnyType)
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
                    // TODO: Both System.String and PhpString
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValuePhpStringCheckEmitter);
                }
                else if (to.SpecialType == SpecialType.System_Object)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValueObjectCheckEmitter);
                }

                // TODO: Specific classes etc. (.AsObject() is ...)
            }

            return new SpecializationInfo(SpecializationKind.Never);
        }
    }
}
