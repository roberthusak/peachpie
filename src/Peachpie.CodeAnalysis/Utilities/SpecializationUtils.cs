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
        private static TypeCheckEmitter CreateTypeCodeEmitter(int typeCode, Func<TypeRefContext, TypeRefMask, TypeRefMask> maskSelector)
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

        private static TypeCheckEmitter CreateMethodEmitter(Func<CodeGenerator, MethodSymbol> methodSelector, Func<TypeRefContext, TypeRefMask, TypeRefMask> maskSelector)
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

        private static TypeCheckEmitter CreateClassCheckEmitter()
        {
            return (cg, expr, to) =>
            {
                Debug.Assert(to.IsReferenceType);

                // arg is To / arg.AsObject() is To

                var place = expr.Place();

                if (place.Type.IsReferenceType)
                {
                    place.EmitLoad(cg.Builder);
                }
                else
                {
                    Debug.Assert(place.Type.Is_PhpValue());

                    place.EmitLoadAddress(cg.Builder);
                    cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.AsObjectNoAlias);
                }

                cg.Builder.EmitOpCode(ILOpCode.Isinst);
                cg.EmitSymbolToken(to, null);

                // Cannot be null if the test succeeds
                var toMask = TypeRefFactory.CreateMask(cg.TypeRefContext, to);
                return cg.TypeRefContext.WithoutNull(toMask);
            };
        }

        // TODO: Aliases are not handled, the dynamic overloading is skipped instead. Handle them as well.
        private static readonly TypeCheckEmitter PhpValueBoolCheckEmitter = CreateTypeCodeEmitter(1, (c, _) => c.GetBooleanTypeMask());
        private static readonly TypeCheckEmitter PhpValueLongCheckEmitter = CreateTypeCodeEmitter(2, (c, _) => c.GetLongTypeMask());
        private static readonly TypeCheckEmitter PhpValueDoubleCheckEmitter = CreateTypeCodeEmitter(3, (c, _) => c.GetDoubleTypeMask());
        private static readonly TypeCheckEmitter PhpValuePhpArrayCheckEmitter = CreateTypeCodeEmitter(4, (c, _) => c.GetArrayTypeMask());
        private static readonly TypeCheckEmitter PhpValueStringCheckEmitter = CreateTypeCodeEmitter(5, (c, _) => c.GetStringTypeMask());
        private static readonly TypeCheckEmitter PhpValueObjectCheckEmitter = CreateTypeCodeEmitter(7, (c, m) => c.GetObjectsFromMask(m));

        private static readonly TypeCheckEmitter PhpValuePhpStringCheckEmitter = CreateMethodEmitter(cg => cg.CoreMethods.PhpValue.IsStringNoAlias, (c, _) => c.GetWritableStringTypeMask());

        private static readonly TypeCheckEmitter ClassCheckEmitter = CreateClassCheckEmitter();

        public static SpecializationInfo GetInfo(PhpCompilation compilation, TypeRefContext typeCtx, BoundExpression expr, TypeSymbol paramType)
        {
            var exprMask = expr.TypeRefMask;
            var paramMask = TypeRefFactory.CreateMask(typeCtx, paramType);

            // Estimate the resulting type of the expression
            var exprTypeEst = EstimateExpressionType(compilation, typeCtx, expr);

            // References are not supported, exclude also obviously different types
            if (exprMask.IsRef || !typeCtx.CanBeSameType(expr.TypeRefMask, paramMask))
            {
                return new SpecializationInfo(SpecializationKind.Never);
            }

            if (paramType.Is_PhpValue())
            {
                return new SpecializationInfo(SpecializationKind.Always);
            }

            if (((exprMask & ~paramMask) == 0 && !exprMask.IsVoid) || exprTypeEst == paramType)
            {
                return new SpecializationInfo(SpecializationKind.Always);
            }
            else if (exprTypeEst.Is_PhpValue())
            {
                if (paramType.SpecialType == SpecialType.System_Boolean)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValueBoolCheckEmitter);
                }
                else if (paramType.SpecialType == SpecialType.System_Int64)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValueLongCheckEmitter);
                }
                else if (paramType.SpecialType == SpecialType.System_Double)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValueDoubleCheckEmitter);
                }
                else if (paramType.Is_PhpArray())
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValuePhpArrayCheckEmitter);
                }
                else if (paramType.SpecialType == SpecialType.System_String)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValueStringCheckEmitter);
                }
                else if (paramType.Is_PhpString())
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValuePhpStringCheckEmitter);
                }
                else if (paramType.SpecialType == SpecialType.System_Object)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValueObjectCheckEmitter);
                }
                else if (paramType.IsReferenceType)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, ClassCheckEmitter);
                }
            }
            else if (exprTypeEst.Is_PhpString() || exprTypeEst.SpecialType == SpecialType.System_String)
            {
                // We can always convert between .NET and PHP strings
                if (paramType.Is_PhpString() || paramType.SpecialType == SpecialType.System_String)
                {
                    return new SpecializationInfo(SpecializationKind.Always);
                }
            }
            else if (exprTypeEst.IsReferenceType && paramType.IsReferenceType)
            {
                if (paramType.IsAssignableFrom(exprTypeEst))
                {
                    return new SpecializationInfo(SpecializationKind.Always);
                }
                else
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, ClassCheckEmitter);
                }
            }

            return new SpecializationInfo(SpecializationKind.Never);
        }

        public static TypeSymbol EstimateExpressionType(PhpCompilation compilation, TypeRefContext typeCtx, BoundExpression expr)
        {
            if (expr.Type is TypeSymbol resolvedType)
            {
                // The actually emitted type
                return resolvedType;
            }
            else
            {
                // Estimate the type from the common cases
                return expr switch
                {
                    BoundVariableRef { Variable: LocalVariableReference { IsOptimized: true, Type: var type } } => type,
                    BoundRoutineCall { TargetMethod: { ReturnType: var type } method } => method.CastToFalse ? compilation.CoreTypes.PhpValue : type,
                    _ => compilation.GetTypeFromTypeRef(typeCtx, expr.TypeRefMask)
                };
            }
        }
    }
}
