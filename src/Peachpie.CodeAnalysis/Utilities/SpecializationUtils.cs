using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Text;
using Microsoft.CodeAnalysis;
using Pchp.CodeAnalysis;
using Pchp.CodeAnalysis.CodeGen;
using Pchp.CodeAnalysis.FlowAnalysis;
using Pchp.CodeAnalysis.FlowAnalysis.Passes.Specialization;
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

    internal static partial class SpecializationUtils
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

        private static TypeCheckEmitter CreateNotNullCheckEmitter()
        {
            return (cg, expr, to) =>
            {
                Debug.Assert(to.IsReferenceType);

                var place = expr.Place();
                Debug.Assert(place.Type.IsReferenceType);

                // arg != null
                cg.EmitNotNull(place);

                // Cannot be null if the test succeeds
                var toMask = TypeRefFactory.CreateMask(cg.TypeRefContext, to);
                return cg.TypeRefContext.WithoutNull(toMask);
            };
        }

        // TODO: Aliases are not handled, the dynamic overloading is skipped instead. Handle them as well.
        private static readonly TypeCheckEmitter PhpValueNullCheckEmitter = CreateTypeCodeEmitter(0, (c, m) => c.GetSystemObjectTypeMask().WithSubclasses);
        private static readonly TypeCheckEmitter PhpValueBoolCheckEmitter = CreateTypeCodeEmitter(1, (c, _) => c.GetBooleanTypeMask());
        private static readonly TypeCheckEmitter PhpValueLongCheckEmitter = CreateTypeCodeEmitter(2, (c, _) => c.GetLongTypeMask());
        private static readonly TypeCheckEmitter PhpValueDoubleCheckEmitter = CreateTypeCodeEmitter(3, (c, _) => c.GetDoubleTypeMask());
        private static readonly TypeCheckEmitter PhpValuePhpArrayCheckEmitter = CreateTypeCodeEmitter(4, (c, _) => c.GetArrayTypeMask());
        private static readonly TypeCheckEmitter PhpValueStringCheckEmitter = CreateTypeCodeEmitter(5, (c, _) => c.GetStringTypeMask());
        private static readonly TypeCheckEmitter PhpValueObjectCheckEmitter = CreateTypeCodeEmitter(7, (c, m) => c.GetSystemObjectTypeMask().WithSubclasses);

        private static readonly TypeCheckEmitter PhpValuePhpStringCheckEmitter = CreateMethodEmitter(
            cg => cg.CoreMethods.PhpValue.IsStringNoAlias,
            (c, _) => c.GetWritableStringTypeMask());

        private static readonly TypeCheckEmitter PhpValuePhpNumberCheckEmitter = CreateMethodEmitter(
            cg => cg.CoreMethods.PhpValue.IsNumberNoAlias,
            (c, _) => c.GetNumberTypeMask());

        private static readonly TypeCheckEmitter ClassCheckEmitter = CreateClassCheckEmitter();

        private static readonly TypeCheckEmitter NotNullCheckEmitter = CreateNotNullCheckEmitter();

        public static SpecializationInfo GetInfo(PhpCompilation compilation, TypeRefContext typeCtx, BoundExpression expr, SpecializedParam paramSpec)
        {
            var paramType = paramSpec.Type;
            var optimization = compilation.Options.ExperimentalOptimization;
            bool isNullParamAllowed = (optimization & ExperimentalOptimization.ForceSpecializedParametersNotNull) == 0;

            var exprMask = EstimateExpressionTypeRefMask(typeCtx, expr);
            var paramMask =
                (paramSpec.Flags & SpecializationFlags.IsNull) != 0
                ? typeCtx.GetNullTypeMask()
                : TypeRefFactory.CreateMask(typeCtx, paramType, !isNullParamAllowed);

            // Estimate the resulting type of the expression
            var exprTypeEst = EstimateExpressionType(compilation, typeCtx, expr, exprMask);

            // References are not supported, exclude also obviously different types
            if (exprMask.IsRef || !typeCtx.CanBeSameType(exprMask, paramMask))
            {
                return new SpecializationInfo(SpecializationKind.Never);
            }

            if (paramType.Is_PhpValue())
            {
                return new SpecializationInfo(SpecializationKind.Always);
            }

            if (((exprMask & ~paramMask) == 0 && !exprMask.IsVoid) || (exprTypeEst == paramType && (isNullParamAllowed || !typeCtx.IsNull(exprMask))))
            {
                return new SpecializationInfo(SpecializationKind.Always);
            }
            else if (exprTypeEst.Is_PhpValue())
            {
                if ((paramSpec.Flags & SpecializationFlags.IsNull) != 0)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValueNullCheckEmitter);
                }
                else if (paramType.SpecialType == SpecialType.System_Boolean)
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
                else if (paramType.Is_PhpNumber() && (optimization & ExperimentalOptimization.AllowPhpNumberRuntimeSpecialization) != 0)
                {
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, PhpValuePhpNumberCheckEmitter);
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
                // We can convert between .NET and PHP strings if allowed in options
                if ((paramType.Is_PhpString() || paramType.SpecialType == SpecialType.System_String)
                    && ((optimization & ExperimentalOptimization.DisableStringParameterCasting) == 0 || exprTypeEst == paramType))
                {
                    return GetReferenceTypeSpecialization();
                }
            }
            else if (exprTypeEst.Is_Class() && paramType.Is_Class())
            {
                if (paramType.IsAssignableFrom(exprTypeEst))
                {
                    return GetReferenceTypeSpecialization();
                }
                else
                {
                    // TODO: Detect when a type can never be the same or inherited from another one
                    return new SpecializationInfo(SpecializationKind.RuntimeDependent, ClassCheckEmitter);
                }
            }

            return new SpecializationInfo(SpecializationKind.Never);

            SpecializationInfo GetReferenceTypeSpecialization() =>
                (isNullParamAllowed || !exprTypeEst.IsReferenceType || !typeCtx.IsNull(exprMask))
                    ? new SpecializationInfo(SpecializationKind.Always)
                    : new SpecializationInfo(SpecializationKind.RuntimeDependent, NotNullCheckEmitter);
        }

        /// <summary>
        /// Estimate <see cref="TypeRefMask"/> for simple expressions (e.g. default parameter values before proper analysis).
        /// </summary>
        public static TypeRefMask EstimateExpressionTypeRefMask(TypeRefContext typeCtx, BoundExpression expr)
        {
            if (!expr.TypeRefMask.IsDefault)
            {
                return expr.TypeRefMask;
            }
            else
            {
                return expr switch
                {
                    BoundLiteral literal => literal.ResolveTypeMask(typeCtx),
                    BoundArrayEx _ => typeCtx.GetArrayTypeMask(),
                    _ => TypeRefMask.AnyType
                };
            }
        }

        public static TypeSymbol EstimateExpressionType(PhpCompilation compilation, TypeRefContext typeCtx, BoundExpression expr, TypeRefMask exprTypeMask = default)
        {
            if (expr.Type is TypeSymbol resolvedType)
            {
                // The actually emitted type
                return resolvedType;
            }
            else
            {
                if (exprTypeMask.IsDefault)
                {
                    exprTypeMask = expr.TypeRefMask;
                }

                // Estimate the type from the common cases
                return expr switch
                {
                    // Stick to the types of temporal variables
                    BoundVariableRef { Variable: LocalVariableReference { Type: var type, VariableKind: var kind } }
                        when kind != VariableKind.Parameter => type,

                    // Original parameter types can change in the routine body (when other type is assigned to the parameter inside the body)
                    BoundVariableRef { Variable: ParameterReference { Parameter: SourceParameterSymbol srcParam } } =>
                        srcParam.NeedsDifferentLocalType(out var type) ? type : srcParam.Type,

                    BoundRoutineCall { TargetMethod: { ReturnType: var type } method } =>
                        method.CastToFalse ? compilation.CoreTypes.PhpValue : type,

                    BoundGlobalConst constant
                        when compilation.GlobalSemantics.ResolveConstant(constant.Name.ToString()) is FieldSymbol constField && constField.IsConst => constField.Type,

                    _ => compilation.GetTypeFromTypeRef(typeCtx, exprTypeMask)
                };
            }
        }

        public static bool IsSpecializationEnabled(ExperimentalOptimization options, SpecializedParam specialization) =>
            (options & ExperimentalOptimization.SpecializeAll) == ExperimentalOptimization.SpecializeAll
            || ((options & ExperimentalOptimization.SpecializeString) != 0
                && specialization.Type.SpecialType == SpecialType.System_String)
            || ((options & ExperimentalOptimization.SpecializePhpString) != 0
                && specialization.Type.Is_PhpString())
            || ((options & ExperimentalOptimization.SpecializeNumbers) != 0
                && (specialization.Type.SpecialType == SpecialType.System_Int64 || specialization.Type.SpecialType == SpecialType.System_Double || specialization.Type.Is_PhpNumber()))
            || ((options & ExperimentalOptimization.SpecializePhpArray) != 0
                && specialization.Type.Is_PhpArray())
            || ((options & ExperimentalOptimization.SpecializeObjects) != 0
                && specialization.Type.SpecialType == SpecialType.System_Object)
            || ((options & ExperimentalOptimization.SpecializeNull) != 0
                && (specialization.Flags & SpecializationFlags.IsNull) != 0)
            || (options & ExperimentalOptimization.SpecializeMiscellaneous) != 0;
    }
}
