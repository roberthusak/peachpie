﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.CodeGen;
using Pchp.CodeAnalysis.Symbols;
using Pchp.Syntax.AST;
using Roslyn.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using BinaryKind = Microsoft.CodeAnalysis.Semantics.BinaryOperationKind;

namespace Pchp.CodeAnalysis.Semantics
{
    partial class BoundExpression
    {
        /// <summary>
        /// Emits the expression with its bound access.
        /// Only Read or None access is possible. Write access has to be handled separately.
        /// </summary>
        /// <param name="cg">Associated code generator.</param>
        /// <returns>The type of expression emitted on top of the evaluation stack.</returns>
        internal virtual TypeSymbol Emit(CodeGenerator cg)
        {
            throw ExceptionUtilities.UnexpectedValue(this.GetType().FullName);
        }
    }

    partial class BoundBinaryEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            Debug.Assert(this.Access.IsRead || this.Access.IsNone);

            // load resulting value directly if resolved:
            if (this.ConstantObject.HasValue)
            {
                if (this.Access.IsNone)
                {
                    return cg.CoreTypes.Void;
                }

                if (this.Access.IsRead)
                {
                    return cg.EmitLoadConstant(this.ConstantObject.Value);
                }
            }

            //
            TypeSymbol returned_type;

            if (UsesOperatorMethod)
            {
                throw new NotImplementedException();    // call this.Operator(Left, Right)
            }

            switch (this.Operation)
            {
                #region Arithmetic Operations

                case Operations.Add:
                    returned_type = (cg.IsLongOnly(this.TypeRefMask)) ? cg.CoreTypes.Long.Symbol : this.Access.TargetType;
                    returned_type = EmitAdd(cg, Left, Right, returned_type);
                    break;

                case Operations.Sub:
                    //Template: "x - y"        Operators.Subtract(x,y) [overloads]
                    returned_type = EmitSub(cg, Left, Right, this.Access.TargetType);
                    break;

                case Operations.Div:
                    //Template: "x / y"
                    returned_type = EmitDivision(cg);
                    break;

                case Operations.Mul:
                    //Template: "x * y"
                    returned_type = EmitMultiply(cg);
                    break;

                case Operations.Pow:
                    //Template: "x ** y"
                    returned_type = EmitPow(cg);
                    break;

                case Operations.Mod:
                    //Template: "x % y"        Operators.Remainder(x,y)

                    //codeGenerator.EmitBoxing(node.LeftExpr.Emit(codeGenerator));
                    //ro_typecode = node.RightExpr.Emit(codeGenerator);
                    //switch (ro_typecode)
                    //{
                    //    case PhpTypeCode.Integer:
                    //        returned_typecode = codeGenerator.EmitMethodCall(Methods.Operators.Remainder.Object_Int32);
                    //        break;

                    //    default:
                    //        codeGenerator.EmitBoxing(ro_typecode);
                    //        returned_typecode = codeGenerator.EmitMethodCall(Methods.Operators.Remainder.Object_Object);
                    //        break;
                    //}
                    //break;
                    throw new NotImplementedException();

                case Operations.ShiftLeft:

                    //// LOAD Operators.ShiftLeft(box left, box right);
                    //codeGenerator.EmitBoxing(node.LeftExpr.Emit(codeGenerator));
                    //codeGenerator.EmitBoxing(node.RightExpr.Emit(codeGenerator));
                    //returned_typecode = codeGenerator.EmitMethodCall(Methods.Operators.ShiftLeft);
                    //break;
                    throw new NotImplementedException();

                case Operations.ShiftRight:

                    //// LOAD Operators.ShiftRight(box left, box right);
                    //codeGenerator.EmitBoxing(node.LeftExpr.Emit(codeGenerator));
                    //codeGenerator.EmitBoxing(node.RightExpr.Emit(codeGenerator));
                    //returned_typecode = codeGenerator.EmitMethodCall(Methods.Operators.ShiftRight);
                    //break;
                    throw new NotImplementedException();

                #endregion

                #region Boolean and Bitwise Operations

                case Operations.And:
                    returned_type = EmitBinaryBooleanOperation(cg, true);
                    break;

                case Operations.Or:
                    returned_type = EmitBinaryBooleanOperation(cg, false);
                    break;

                case Operations.Xor:
                    returned_type = EmitBinaryXor(cg);
                    break;

                case Operations.BitAnd:
                    //returned_type = EmitBitAnd(cg, Left, Right);
                    //break;
                    throw new NotImplementedException();

                case Operations.BitOr:
                    returned_type = EmitBitOr(cg, Left, Right);
                    break;
                    
                case Operations.BitXor:
                    //returned_typecode = EmitBitOperation(node, codeGenerator, Operators.BitOp.Xor);
                    //break;
                    throw new NotImplementedException();

                #endregion

                #region Comparing Operations

                case Operations.Equal:
                    returned_type = EmitEquality(cg);
                    break;

                case Operations.NotEqual:
                    EmitEquality(cg);
                    cg.EmitLogicNegation();
                    returned_type = cg.CoreTypes.Boolean;
                    break;

                case Operations.GreaterThan:
                    returned_type = EmitLtGt(cg, false);
                    break;

                case Operations.LessThan:
                    returned_type = EmitLtGt(cg, true);
                    break;

                case Operations.GreaterThanOrEqual:
                    // template: !(LessThan)
                    returned_type = EmitLtGt(cg, true);
                    cg.EmitLogicNegation();
                    break;

                case Operations.LessThanOrEqual:
                    // template: !(GreaterThan)
                    returned_type = EmitLtGt(cg, false);
                    cg.EmitLogicNegation();
                    break;

                case Operations.Identical:

                    // Left === Right
                    returned_type = EmitStrictEquality(cg);
                    break;

                case Operations.NotIdentical:

                    // ! (Left === Right)
                    returned_type = EmitStrictEquality(cg);
                    cg.EmitLogicNegation();
                    break;

                #endregion

                default:
                    throw ExceptionUtilities.Unreachable;
            }

            //
            switch (Access.Flags)
            {
                case AccessMask.Read:
                    // Result is read, do nothing.
                    Debug.Assert(returned_type.SpecialType != SpecialType.System_Void);
                    break;

                case AccessMask.None:
                    // Result is not read, pop the result
                    cg.EmitPop(returned_type);
                    returned_type = cg.CoreTypes.Void;
                    break;
            }

            //
            return returned_type;
        }

        /// <summary>
        /// Emits <c>+</c> operator suitable for actual operands.
        /// </summary>
        private static TypeSymbol EmitAdd(CodeGenerator cg, BoundExpression left, BoundExpression right, TypeSymbol resultTypeOpt = null)
        {
            // Template: x + y
            return EmitAdd(cg, cg.Emit(left), right, resultTypeOpt);
        }

        /// <summary>
        /// Emits <c>+</c> operator suitable for actual operands.
        /// </summary>
        internal static TypeSymbol EmitAdd(CodeGenerator cg, TypeSymbol xtype, BoundExpression Right, TypeSymbol resultTypeOpt = null)
        {
            var il = cg.Builder;

            xtype = cg.EmitConvertIntToLong(xtype);    // int|bool -> long

            //
            if (xtype == cg.CoreTypes.PhpNumber)
            {
                var ytype = cg.EmitConvertIntToLong(cg.Emit(Right));  // int|bool -> long

                if (ytype == cg.CoreTypes.PhpNumber)
                {
                    // number + number : number
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_number_number)
                        .Expect(cg.CoreTypes.PhpNumber);
                }
                else if (ytype.SpecialType == SpecialType.System_Double)
                {
                    // number + r8 : r8
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_number_double)
                        .Expect(SpecialType.System_Double);
                }
                else if (ytype.SpecialType == SpecialType.System_Int64)
                {
                    // number + long : number
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_number_long)
                        .Expect(cg.CoreTypes.PhpNumber);
                }
                else if (ytype == cg.CoreTypes.PhpValue)
                {
                    // number + value : number
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_number_value)
                        .Expect(cg.CoreTypes.PhpNumber);
                }

                //
                throw new NotImplementedException($"Add(number, {ytype.Name})");
            }
            else if (xtype.SpecialType == SpecialType.System_Double)
            {
                var ytype = cg.EmitConvertNumberToDouble(Right); // bool|int|long|number -> double

                if (ytype.SpecialType == SpecialType.System_Double)
                {
                    // r8 + r8 : r8
                    il.EmitOpCode(ILOpCode.Add);
                    return cg.CoreTypes.Double;
                }
                else if (ytype == cg.CoreTypes.PhpValue)
                {
                    // r8 + value : r8
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_double_value)
                        .Expect(SpecialType.System_Double);
                }

                //
                throw new NotImplementedException($"Add(double, {ytype.Name})");
            }
            else if (xtype.SpecialType == SpecialType.System_Int64)
            {
                var ytype = cg.EmitConvertIntToLong(cg.Emit(Right));    // int|bool -> long

                if (ytype.SpecialType == SpecialType.System_Int64)
                {
                    if (resultTypeOpt != null)
                    {
                        if (resultTypeOpt.SpecialType == SpecialType.System_Int64)
                        {
                            // (long)(i8 + i8 : number)
                            il.EmitOpCode(ILOpCode.Add);
                            return cg.CoreTypes.Long;
                        }
                    }

                    // i8 + i8 : number
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_long_long)
                        .Expect(cg.CoreTypes.PhpNumber);
                }
                else if (ytype.SpecialType == SpecialType.System_Double)
                {
                    // i8 + r8 : r8
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_long_double)
                        .Expect(SpecialType.System_Double);
                }
                else if (ytype == cg.CoreTypes.PhpNumber)
                {
                    // i8 + number : number
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_long_number)
                        .Expect(cg.CoreTypes.PhpNumber);
                }
                else if (ytype == cg.CoreTypes.PhpValue)
                {
                    // i8 + value : number
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_long_value)
                        .Expect(cg.CoreTypes.PhpNumber);
                }

                //
                throw new NotImplementedException($"Add(int64, {ytype.Name})");
            }
            else if (xtype == cg.CoreTypes.PhpValue)
            {
                var ytype = cg.EmitConvertIntToLong(cg.Emit(Right));    // int|bool -> long

                if (ytype.SpecialType == SpecialType.System_Int64)
                {
                    // value + i8 : number
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_value_long)
                        .Expect(cg.CoreTypes.PhpNumber);
                }
                else if (ytype.SpecialType == SpecialType.System_Double)
                {
                    // value + r8 : r8
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_value_double)
                        .Expect(SpecialType.System_Double);
                }
                else if (ytype == cg.CoreTypes.PhpNumber)
                {
                    // value + number : number
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_value_number)
                        .Expect(cg.CoreTypes.PhpNumber);
                }
                else if (ytype == cg.CoreTypes.PhpValue)
                {
                    // value + value : value
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Add_value_value)
                        .Expect(cg.CoreTypes.PhpValue);
                }

                //
                throw new NotImplementedException($"Add(PhpValue, {ytype.Name})");
            }

            //
            throw new NotImplementedException($"Add({xtype.Name}, ...)");
        }

        /// <summary>
        /// Emits subtraction operator.
        /// </summary>
        internal static TypeSymbol EmitSub(CodeGenerator cg, BoundExpression left, BoundExpression right, TypeSymbol resultTypeOpt = null)
        {
            return EmitSub(cg, cg.Emit(left), right, resultTypeOpt);
        }

        /// <summary>
        /// Emits subtraction operator.
        /// </summary>
        internal static TypeSymbol EmitSub(CodeGenerator cg, TypeSymbol xtype, BoundExpression right, TypeSymbol resultTypeOpt = null)
        {
            var il = cg.Builder;

            xtype = cg.EmitConvertIntToLong(xtype);    // int|bool -> int64
            TypeSymbol ytype;

            switch (xtype.SpecialType)
            {
                case SpecialType.System_Int64:
                    ytype = cg.EmitConvertIntToLong(cg.Emit(right));
                    if (ytype.SpecialType == SpecialType.System_Int64)
                    {
                        // i8 - i8 : number
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Subtract_long_long)
                            .Expect(cg.CoreTypes.PhpNumber);
                    }
                    else if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // i8 - r8 : r8
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Subtract_long_double)
                            .Expect(cg.CoreTypes.Double);
                    }
                    else if (ytype == cg.CoreTypes.PhpNumber)
                    {
                        // i8 - number : number
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Subtract_long_number)
                            .Expect(cg.CoreTypes.PhpNumber);
                    }
                    throw new NotImplementedException($"Sub(long, {ytype.Name})");
                case SpecialType.System_Double:
                    ytype = cg.EmitConvertNumberToDouble(right); // bool|int|long|number -> double
                    if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // r8 - r8 : r8
                        il.EmitOpCode(ILOpCode.Sub);
                        return cg.CoreTypes.Double;
                    }
                    throw new NotImplementedException($"Sub(double, {ytype.Name})");
                default:
                    if (xtype == cg.CoreTypes.PhpNumber)
                    {
                        ytype = cg.EmitConvertIntToLong(cg.Emit(right));
                        if (ytype.SpecialType == SpecialType.System_Int64)
                        {
                            // number - i8 : number
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Subtract_number_long)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }
                        else if (ytype.SpecialType == SpecialType.System_Double)
                        {
                            // number - r8 : double
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Subtract_number_double)
                                .Expect(SpecialType.System_Double);
                        }
                        else if (ytype == cg.CoreTypes.PhpNumber)
                        {
                            // number - number : number
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Subtract_number_number)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }

                        throw new NotImplementedException($"Sub(PhpNumber, {ytype.Name})");
                    }
                    else if (xtype == cg.CoreTypes.PhpValue)
                    {
                        ytype = cg.EmitConvertIntToLong(cg.Emit(right));

                        if (ytype.SpecialType == SpecialType.System_Int64)
                        {
                            // value - i8 : number
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Subtract_value_long)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }
                        else if (ytype.SpecialType == SpecialType.System_Double)
                        {
                            // value - r8 : r8
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Subtract_value_double)
                                .Expect(SpecialType.System_Double);
                        }
                        else if (ytype == cg.CoreTypes.PhpNumber)
                        {
                            // value - number : number
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Subtract_value_number)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }
                        else if (ytype == cg.CoreTypes.PhpValue)
                        {
                            // value - value : number
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Subtract_value_value)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }

                        throw new NotImplementedException($"Sub(PhpValue, {ytype.Name})");
                    }

                    throw new NotImplementedException($"Sub({xtype.Name},...)");
            }
        }

        internal static TypeSymbol EmitBitOr(CodeGenerator cg, BoundExpression left, BoundExpression right)
        {
            // most common cases:
            if (cg.IsLongOnly(left.TypeRefMask) || cg.IsLongOnly(right.TypeRefMask))
            {
                // i64 | i64 : i64
                cg.EmitConvert(left, cg.CoreTypes.Long);
                cg.EmitConvert(right, cg.CoreTypes.Long);
                cg.Builder.EmitOpCode(ILOpCode.Or);
                return cg.CoreTypes.Long;
            }
            
            //
            return EmitBitOr(cg, cg.Emit(left), right);
        }

        internal static TypeSymbol EmitBitOr(CodeGenerator cg, TypeSymbol xtype, BoundExpression right)
        {
            switch (xtype.SpecialType)
            {
                case SpecialType.System_Void:
                case SpecialType.System_Int32:
                case SpecialType.System_Boolean:
                case SpecialType.System_Double:
                    cg.EmitConvert(xtype, 0, cg.CoreTypes.Long);
                    goto case SpecialType.System_Int64;

                case SpecialType.System_Int64:
                    cg.EmitConvert(right, cg.CoreTypes.Long);
                    cg.Builder.EmitOpCode(ILOpCode.Or);
                    return cg.CoreTypes.Long;

                case SpecialType.System_String:
                    throw new NotImplementedException();    // string | string or string | long

                default:
                    if (right.ResultType != null && right.ResultType.SpecialType != SpecialType.System_String)
                    {
                        // value | !string -> long | long -> long
                        cg.EmitConvert(xtype, 0, cg.CoreTypes.Long);
                        goto case SpecialType.System_Int64;
                    }

                    cg.EmitConvert(xtype, 0, cg.CoreTypes.PhpValue);
                    cg.EmitConvert(right, cg.CoreTypes.PhpValue);
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.BitwiseOr_PhpValue_PhpValue)
                        .Expect(cg.CoreTypes.PhpValue);
            }
        }

        /// <summary>
        /// Emits binary boolean operation (AND or OR).
        /// </summary>
        /// <param name="cg">A code generator.</param>
        /// <param name="isAnd">Whether to emit AND, otherwise OR.</param>
        /// <returns>A type code of the result.</returns>
        TypeSymbol EmitBinaryBooleanOperation(CodeGenerator cg, bool isAnd)
        {
            var boolean = cg.CoreTypes.Boolean;  // typeof(bool)

            var il = cg.Builder;
            var partial_eval_label = new object();
            var end_label = new object();

            // IF [!]<(bool) Left> THEN GOTO partial_eval;
            cg.EmitConvert(Left, cg.CoreTypes.Boolean);
            il.EmitBranch(isAnd ? ILOpCode.Brfalse : ILOpCode.Brtrue, partial_eval_label);

            // <RESULT> = <(bool) Right>;
            cg.EmitConvert(Right, cg.CoreTypes.Boolean);

            // GOTO end;
            il.EmitBranch(ILOpCode.Br, end_label);
            il.AdjustStack(-1); // workarounds assert in ILBuilder.MarkLabel, we're doing something wrong with ILBuilder

            // partial_eval:
            il.MarkLabel(partial_eval_label);
            il.EmitOpCode(isAnd ? ILOpCode.Ldc_i4_0 : ILOpCode.Ldc_i4_1, 1);

            // end:
            il.MarkLabel(end_label);

            //
            return boolean;
        }

        /// <summary>
        /// Emits binary operation XOR.
        /// </summary>
        TypeSymbol EmitBinaryXor(CodeGenerator cg)
        {
            // LOAD <(bool) leftSon> == <(bool) rightSon>;
            cg.EmitConvert(Left, cg.CoreTypes.Boolean);
            cg.EmitConvert(Right, cg.CoreTypes.Boolean);
            cg.EmitOpCode(ILOpCode.Ceq);

            cg.EmitOpCode(ILOpCode.Ldc_i4_0);
            cg.EmitOpCode(ILOpCode.Ceq);

            return cg.CoreTypes.Boolean;
        }

        /// <summary>
        /// Emits check for values equality.
        /// Lefts <c>bool</c> on top of evaluation stack.
        /// </summary>
        TypeSymbol EmitEquality(CodeGenerator cg)
        {
            // x == y
            return EmitEquality(cg, Left, Right);
        }

        /// <summary>
        /// Emits check for values equality.
        /// Lefts <c>bool</c> on top of evaluation stack.
        /// </summary>
        internal static TypeSymbol EmitEquality(CodeGenerator cg, BoundExpression left, BoundExpression right)
        {
            return EmitEquality(cg, cg.Emit(left), right);
        }

        /// <summary>
        /// Emits check for values equality.
        /// Lefts <c>bool</c> on top of evaluation stack.
        /// </summary>
        internal static TypeSymbol EmitEquality(CodeGenerator cg, TypeSymbol xtype, BoundExpression right)
        {
            TypeSymbol ytype;

            switch (xtype.SpecialType)
            {
                case SpecialType.System_Boolean:

                    // bool == y.ToBoolean()
                    cg.EmitConvert(right, cg.CoreTypes.Boolean);
                    cg.Builder.EmitOpCode(ILOpCode.Ceq);

                    return cg.CoreTypes.Boolean;

                case SpecialType.System_Int32:
                    // i4 -> i8
                    cg.Builder.EmitOpCode(ILOpCode.Conv_i8);
                    goto case SpecialType.System_Int64;

                case SpecialType.System_Int64:

                    ytype = cg.Emit(right);

                    //
                    if (ytype.SpecialType == SpecialType.System_Int32)
                    {
                        cg.Builder.EmitOpCode(ILOpCode.Conv_i8);    // i4 -> i8
                        ytype = cg.CoreTypes.Long;
                    }

                    //
                    if (ytype.SpecialType == SpecialType.System_Int64)
                    {
                        // i8 == i8
                        cg.Builder.EmitOpCode(ILOpCode.Ceq);
                        return cg.CoreTypes.Boolean;
                    }
                    else if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // i8 == r8
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Ceq_long_double)
                            .Expect(SpecialType.System_Boolean);
                    }
                    else if (ytype.SpecialType == SpecialType.System_Boolean)
                    {
                        // i8 == bool
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Ceq_long_bool)
                            .Expect(SpecialType.System_Boolean);
                    }
                    else if (ytype.SpecialType == SpecialType.System_String)
                    {
                        // i8 == string
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Ceq_long_string)
                            .Expect(SpecialType.System_Boolean);
                    }

                    // value
                    ytype = cg.EmitConvertToPhpValue(ytype, 0);

                    // compare(i8, value) == 0
                    cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Compare_long_value);
                    cg.EmitLogicNegation();

                    return cg.CoreTypes.Boolean;

                case SpecialType.System_Double:

                    ytype = cg.EmitConvertNumberToDouble(right);  // bool|long|int -> double

                    if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // r8 == r8
                        cg.Builder.EmitOpCode(ILOpCode.Ceq);
                        return cg.CoreTypes.Boolean;
                    }
                    else if (ytype.SpecialType == SpecialType.System_String)
                    {
                        // r8 == string
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Ceq_double_string)
                            .Expect(SpecialType.System_Boolean);
                    }

                    // value
                    ytype = cg.EmitConvertToPhpValue(ytype, 0);

                    // compare(double, value) == 0
                    cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Compare_double_value);
                    cg.EmitLogicNegation();

                    return cg.CoreTypes.Boolean;

                case SpecialType.System_String:

                    ytype = cg.Emit(right);

                    if (ytype.SpecialType == SpecialType.System_Int32)
                    {
                        // i4 -> i8
                        cg.Builder.EmitOpCode(ILOpCode.Conv_i8);
                        ytype = cg.CoreTypes.Long;
                    }

                    if (ytype.SpecialType == SpecialType.System_Int64)
                    {
                        // string == i8
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Ceq_string_long)
                            .Expect(SpecialType.System_Boolean);
                    }
                    else if (ytype.SpecialType == SpecialType.System_Boolean)
                    {
                        // string == bool
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Ceq_string_bool)
                            .Expect(SpecialType.System_Boolean);
                    }
                    else if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // string == r8
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Ceq_string_double)
                            .Expect(SpecialType.System_Boolean);
                    }

                    // value
                    ytype = cg.EmitConvertToPhpValue(ytype, 0);

                    // compare(string, value) == 0
                    cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Compare_string_value);
                    cg.EmitLogicNegation();
                    return cg.CoreTypes.Boolean;

                //case SpecialType.System_Object:
                //    goto default;

                default:

                    if (xtype == cg.CoreTypes.PhpNumber)
                    {
                        ytype = cg.EmitConvertIntToLong(cg.Emit(right));
                        if (ytype.SpecialType == SpecialType.System_Int64)
                        {
                            // number == i8
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Eq_number_long)
                                .Expect(SpecialType.System_Boolean);
                        }
                        else if (ytype.SpecialType == SpecialType.System_Double)
                        {
                            // number == r8
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Eq_number_double)
                                .Expect(SpecialType.System_Boolean);
                        }
                        else if (ytype == cg.CoreTypes.PhpNumber)
                        {
                            // number == number
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Eq_number_number)
                                .Expect(SpecialType.System_Boolean);
                        }
                        else
                        {
                            throw new NotImplementedException($"PhpNumber == {ytype}");
                        }
                    }
                    else
                    {
                        // TODO: xtype: PhpArray, ...

                        xtype = cg.EmitConvertToPhpValue(xtype, 0);

                        // TODO: overloads for type of <right>

                        ytype = cg.EmitConvertToPhpValue(cg.Emit(right), right.TypeRefMask);

                        // value == value
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.Eq_PhpValue_PhpValue)
                            .Expect(SpecialType.System_Boolean);
                    }
            }
        }

        TypeSymbol EmitStrictEquality(CodeGenerator cg)
            => EmitStrictEquality(cg, Left, Right);

        internal static TypeSymbol EmitStrictEquality(CodeGenerator cg, BoundExpression left, BoundExpression right)
            => EmitStrictEquality(cg, cg.Emit(left), right);

        internal static TypeSymbol EmitStrictEquality(CodeGenerator cg, TypeSymbol xtype, BoundExpression right)
        {
            TypeSymbol ytype;

            switch (xtype.SpecialType)
            {
                case SpecialType.System_Boolean:
                    ytype = cg.Emit(right);
                    if (ytype.SpecialType == SpecialType.System_Boolean)
                    {
                        // bool == bool
                        cg.Builder.EmitOpCode(ILOpCode.Ceq);
                        return cg.CoreTypes.Boolean;
                    }
                    else if (
                        ytype.SpecialType == SpecialType.System_Double ||
                        ytype.SpecialType == SpecialType.System_Int32 ||
                        ytype.SpecialType == SpecialType.System_Int64 ||
                        ytype.SpecialType == SpecialType.System_String ||
                        ytype.IsOfType(cg.CoreTypes.IPhpArray) ||
                        ytype == cg.CoreTypes.PhpString ||
                        ytype == cg.CoreTypes.Object)
                    {
                        // bool == something else => false
                        cg.EmitPop(ytype);
                        cg.EmitPop(xtype);
                        cg.Builder.EmitBoolConstant(false);
                        return cg.CoreTypes.Boolean;
                    }
                    else
                    {
                        // bool == PhpValue
                        cg.EmitConvertToPhpValue(ytype, 0);
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.StrictCeq_bool_PhpValue)
                            .Expect(SpecialType.System_Boolean);
                    }

                case SpecialType.System_Int32:
                    cg.Builder.EmitOpCode(ILOpCode.Conv_i8);    // i4 -> i8
                    goto case SpecialType.System_Int64;

                case SpecialType.System_Int64:
                    ytype = cg.Emit(right);
                    if (ytype.SpecialType == SpecialType.System_Int32)
                    {
                        cg.Builder.EmitOpCode(ILOpCode.Conv_i8);    // i4 -> i8
                        ytype = cg.CoreTypes.Long;
                    }

                    if (ytype.SpecialType == SpecialType.System_Int64)
                    {
                        // i8 == i8
                        cg.Builder.EmitOpCode(ILOpCode.Ceq);
                        return cg.CoreTypes.Boolean;
                    }
                    else if (
                        ytype.SpecialType == SpecialType.System_Boolean ||
                        ytype.SpecialType == SpecialType.System_String ||
                        ytype.SpecialType == SpecialType.System_Double ||
                        ytype.IsOfType(cg.CoreTypes.IPhpArray) ||
                        ytype == cg.CoreTypes.Object ||
                        ytype == cg.CoreTypes.PhpString)
                    {
                        // i8 == something else => false
                        cg.EmitPop(ytype);
                        cg.EmitPop(xtype);
                        cg.Builder.EmitBoolConstant(false);
                        return cg.CoreTypes.Boolean;
                    }
                    else
                    {
                        // i8 == PhpValue
                        cg.EmitConvertToPhpValue(ytype, 0);
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.StrictCeq_long_PhpValue)
                            .Expect(SpecialType.System_Boolean);
                    }

                case SpecialType.System_Double:
                    ytype = cg.Emit(right);
                    if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // r8 == r8
                        cg.Builder.EmitOpCode(ILOpCode.Ceq);
                        return cg.CoreTypes.Boolean;
                    }
                    else if (
                        ytype.SpecialType == SpecialType.System_Boolean ||
                        ytype.SpecialType == SpecialType.System_String ||
                        ytype.SpecialType == SpecialType.System_Int64 ||
                        ytype.SpecialType == SpecialType.System_Int32 ||
                        ytype.IsOfType(cg.CoreTypes.IPhpArray) ||
                        ytype == cg.CoreTypes.Object ||
                        ytype == cg.CoreTypes.PhpString)
                    {
                        // r8 == something else => false
                        cg.EmitPop(ytype);
                        cg.EmitPop(xtype);
                        cg.Builder.EmitBoolConstant(false);
                        return cg.CoreTypes.Boolean;
                    }
                    else
                    {
                        // r8 == PhpValue
                        cg.EmitConvertToPhpValue(ytype, 0);
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.StrictCeq_double_PhpValue)
                            .Expect(SpecialType.System_Boolean);
                    }

                default:

                    // TODO: PhpArray, Object === ...

                    xtype = cg.EmitConvertToPhpValue(xtype, 0);

                    ytype = cg.Emit(right);

                    if (ytype.SpecialType == SpecialType.System_Boolean)
                    {
                        // PhpValue == bool
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.StrictCeq_PhpValue_bool)
                            .Expect(SpecialType.System_Boolean);
                    }
                    else
                    {
                        ytype = cg.EmitConvertToPhpValue(ytype, 0);

                        // PhpValue == PhpValue
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.StrictCeq_PhpValue_PhpValue)
                            .Expect(SpecialType.System_Boolean);
                    }
            }
        }

        /// <summary>
        /// Emits comparison operator pushing <c>bool</c> (<c>i4</c> of value <c>0</c> or <c>1</c>) onto the evaluation stack.
        /// </summary>
        /// <param name="cg">Code generator helper.</param>
        /// <param name="lt">True for <c>clt</c> (less than) otherwise <c>cgt</c> (greater than).</param>
        /// <returns>Resulting type code pushed onto the top of evaliuation stack.</returns>
        TypeSymbol EmitLtGt(CodeGenerator cg, bool lt)
            => EmitLtGt(cg, cg.Emit(Left), Right, lt);

        /// <summary>
        /// Emits comparison operator pushing <c>bool</c> (<c>i4</c> of value <c>0</c> or <c>1</c>) onto the evaluation stack.
        /// </summary>
        /// <param name="cg">Code generator helper.</param>
        /// <param name="lt">True for <c>clt</c> (less than) otherwise <c>cgt</c> (greater than).</param>
        /// <returns>Resulting type code pushed onto the top of evaliuation stack.</returns>
        internal static TypeSymbol EmitLtGt(CodeGenerator cg, TypeSymbol xtype, BoundExpression right, bool lt)
        {
            TypeSymbol ytype;
            var il = cg.Builder;

            switch (xtype.SpecialType)
            {
                case SpecialType.System_Void:
                    // Operators.CompareNull(value)
                    throw new NotImplementedException();

                case SpecialType.System_Int32:
                    // i4 -> i8
                    il.EmitOpCode(ILOpCode.Conv_i8);
                    goto case SpecialType.System_Int64;

                case SpecialType.System_Int64:
                    ytype = cg.EmitConvertIntToLong(cg.Emit(right));    // bool|int -> long
                    if (ytype.SpecialType == SpecialType.System_Int64)
                    {
                        il.EmitOpCode(lt ? ILOpCode.Clt : ILOpCode.Cgt);
                    }
                    else if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // i8 <> r8
                        return cg.EmitCall(ILOpCode.Call, lt
                            ? cg.CoreMethods.Operators.Clt_long_double
                            : cg.CoreMethods.Operators.Cgt_long_double);
                    }
                    else
                    {
                        ytype = cg.EmitConvertToPhpValue(ytype, 0);

                        // compare(i8, value) <> 0
                        cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Compare_long_value);

                        il.EmitOpCode(ILOpCode.Ldc_i4_0, 1);
                        il.EmitOpCode(lt ? ILOpCode.Clt : ILOpCode.Cgt);
                    }
                    return cg.CoreTypes.Boolean;

                case SpecialType.System_Double:
                    ytype = cg.EmitConvertNumberToDouble(right);    // bool|int|long|number -> double
                    if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // r8 <> r8
                        il.EmitOpCode(lt ? ILOpCode.Clt : ILOpCode.Cgt);
                    }
                    else
                    {
                        // compare(r8, value)
                        ytype = cg.EmitConvertToPhpValue(ytype, 0);
                        cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Compare_double_value);

                        // <> 0
                        il.EmitOpCode(ILOpCode.Ldc_i4_0, 1);
                        il.EmitOpCode(lt ? ILOpCode.Clt : ILOpCode.Cgt);
                    }
                    return cg.CoreTypes.Boolean;

                case SpecialType.System_String:
                    ytype = cg.Emit(right);
                    if (ytype.SpecialType == SpecialType.System_String)
                    {
                        // compare(string, string)
                        cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Compare_string_string);
                    }
                    else if (ytype.SpecialType == SpecialType.System_Int64)
                    {
                        // compare(string, long)
                        cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Compare_string_long);
                    }
                    else if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // compare(string, double)
                        cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Compare_string_double);
                    }
                    else
                    {
                        // compare(string, value)
                        ytype = cg.EmitConvertToPhpValue(ytype, 0);
                        cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Compare_string_value);
                    }

                    // <> 0
                    il.EmitOpCode(ILOpCode.Ldc_i4_0, 1);
                    il.EmitOpCode(lt ? ILOpCode.Clt : ILOpCode.Cgt);
                    return cg.CoreTypes.Boolean;

                case SpecialType.System_Boolean:

                    cg.EmitConvert(right, cg.CoreTypes.Boolean);
                    ytype = cg.CoreTypes.Boolean;

                    // compare(bool, bool)
                    cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Compare_bool_bool);

                    // <> 0
                    il.EmitOpCode(ILOpCode.Ldc_i4_0, 1);
                    il.EmitOpCode(lt ? ILOpCode.Clt : ILOpCode.Cgt);
                    return cg.CoreTypes.Boolean;

                default:
                    if (xtype == cg.CoreTypes.PhpNumber)
                    {
                        ytype = cg.EmitConvertIntToLong(cg.Emit(right));    // bool|int -> long
                        if (ytype.SpecialType == SpecialType.System_Int64)
                        {
                            // number <> i8
                            return cg.EmitCall(ILOpCode.Call, lt
                                ? cg.CoreMethods.PhpNumber.lt_number_long
                                : cg.CoreMethods.PhpNumber.gt_number_long)
                                .Expect(SpecialType.System_Boolean);
                        }
                        else if (ytype.SpecialType == SpecialType.System_Double)
                        {
                            // number <> r8
                            return cg.EmitCall(ILOpCode.Call, lt
                                ? cg.CoreMethods.PhpNumber.lt_number_double
                                : cg.CoreMethods.PhpNumber.gt_number_double)
                                .Expect(SpecialType.System_Boolean);
                        }
                        else if (ytype == cg.CoreTypes.PhpNumber)
                        {
                            // number <> number
                            return cg.EmitCall(ILOpCode.Call, lt
                                ? cg.CoreMethods.PhpNumber.lt_number_number
                                : cg.CoreMethods.PhpNumber.gt_number_number)
                                .Expect(SpecialType.System_Boolean);
                        }
                        else
                        {
                            ytype = cg.EmitConvertToPhpValue(ytype, 0);

                            // compare(number, value)
                            cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Compare_number_value);

                            // <> 0
                            il.EmitOpCode(ILOpCode.Ldc_i4_0, 1);    // +1 on stack
                            il.EmitOpCode(lt ? ILOpCode.Clt : ILOpCode.Cgt);
                            return cg.CoreTypes.Boolean;
                        }
                    }
                    else
                    {
                        xtype = cg.EmitConvertToPhpValue(xtype, 0);
                        ytype = cg.Emit(right);

                        // TODO: if (ytype.SpecialType == SpecialType.System_Boolean) ...
                        // TODO: if (ytype.SpecialType == SpecialType.System_Int64) ...
                        // TODO: if (ytype.SpecialType == SpecialType.System_String) ...
                        // TODO: if (ytype.SpecialType == SpecialType.System_Double) ...

                        // compare(value, value)
                        ytype = cg.EmitConvertToPhpValue(ytype, right.TypeRefMask);
                        cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Compare_value_value);

                        // <> 0
                        il.EmitOpCode(ILOpCode.Ldc_i4_0, 1);
                        il.EmitOpCode(lt ? ILOpCode.Clt : ILOpCode.Cgt);
                        return cg.CoreTypes.Boolean;
                    }
            }
        }

        /// <summary>
        /// Emits <c>*</c> operation.
        /// </summary>
        TypeSymbol EmitMultiply(CodeGenerator cg)
            => EmitMul(cg, cg.Emit(Left), Right);

        internal static TypeSymbol EmitMul(CodeGenerator cg, TypeSymbol xtype, BoundExpression right, TypeSymbol resultTypeOpt = null)
        {
            var il = cg.Builder;

            xtype = cg.EmitConvertIntToLong(xtype);    // int|bool -> int64

            TypeSymbol ytype;

            switch (xtype.SpecialType)
            {
                case SpecialType.System_Double:
                    ytype = cg.EmitConvertNumberToDouble(right); // bool|int|long|number -> double
                    if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // r8 * r8 : r8
                        il.EmitOpCode(ILOpCode.Mul);
                        return xtype;   // r8
                    }
                    else if (ytype == cg.CoreTypes.PhpValue)
                    {
                        // r8 * value : r8
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_double_value)
                                .Expect(SpecialType.System_Double);
                    }
                    //
                    throw new NotImplementedException($"Mul(double, {ytype.Name})");
                case SpecialType.System_Int64:
                    ytype = cg.EmitConvertIntToLong(cg.Emit(right));
                    if (ytype.SpecialType == SpecialType.System_Int64)
                    {
                        // i8 * i8 : number
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_long_long)
                                .Expect(cg.CoreTypes.PhpNumber);
                    }
                    else if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // i8 * r8 : r8
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_long_double)
                                .Expect(SpecialType.System_Double);
                    }
                    else if (ytype == cg.CoreTypes.PhpNumber)
                    {
                        // i8 * number : number
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_long_number)
                                .Expect(cg.CoreTypes.PhpNumber);
                    }
                    else if (ytype == cg.CoreTypes.PhpValue)
                    {
                        // i8 * value : number
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_long_value)
                                .Expect(cg.CoreTypes.PhpNumber);
                    }
                    //
                    throw new NotImplementedException($"Mul(int64, {ytype.Name})");
                default:
                    if (xtype == cg.CoreTypes.PhpNumber)
                    {
                        ytype = cg.EmitConvertIntToLong(cg.Emit(right));

                        if (ytype.SpecialType == SpecialType.System_Int64)
                        {
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_number_long)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }
                        else if (ytype.SpecialType == SpecialType.System_Double)
                        {
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_number_double)
                                .Expect(cg.CoreTypes.Double);
                        }
                        else if (ytype == cg.CoreTypes.PhpNumber)
                        {
                            // number * number : number
                            cg.EmitConvertToPhpNumber(ytype, right.TypeRefMask);
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_number_number)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }
                        else if (ytype == cg.CoreTypes.PhpValue)
                        {
                            // number * value : number
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_number_value)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }
                        else
                        {
                            // TODO: unconvertible

                            // number * number : number
                            cg.EmitConvertToPhpNumber(ytype, right.TypeRefMask);
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_number_number)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }
                        //
                        throw new NotImplementedException($"Mul(PhpNumber, {ytype.Name})");
                    }
                    else if (xtype == cg.CoreTypes.PhpValue)
                    {
                        ytype = cg.EmitConvertIntToLong(cg.Emit(right));    // bool|int -> long
                        if (ytype == cg.CoreTypes.PhpValue)
                        {
                            // value * value : number
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_value_value)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }
                        else if (ytype == cg.CoreTypes.PhpNumber)
                        {
                            // value * number : number
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_value_number)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }
                        else if (ytype == cg.CoreTypes.Long)
                        {
                            // value * i8 : number
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_value_long)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }
                        else if (ytype == cg.CoreTypes.Double)
                        {
                            // value * r8 : double
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Mul_value_double)
                                .Expect(SpecialType.System_Double);
                        }
                        //
                        throw new NotImplementedException($"Mul(PhpValue, {ytype.Name})");
                    }

                    //
                    throw new NotImplementedException($"Mul({xtype.Name}, ...)");
            }
        }

        /// <summary>
        /// Emits <c>/</c> operator.
        /// </summary>
        TypeSymbol EmitDivision(CodeGenerator cg)
            => EmitDiv(cg, cg.Emit(Left), Right);

        internal static TypeSymbol EmitDiv(CodeGenerator cg, TypeSymbol xtype, BoundExpression right, TypeSymbol resultTypeOpt = null)
        {
            var il = cg.Builder;

            xtype = cg.EmitConvertIntToLong(xtype);    // int|bool -> int64
            TypeSymbol ytype;

            switch (xtype.SpecialType)
            {
                case SpecialType.System_Double:
                    ytype = cg.EmitConvertNumberToDouble(right); // bool|int|long|number -> double
                    if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        il.EmitOpCode(ILOpCode.Div);
                        return xtype;   // r8
                    }

                    throw new NotImplementedException();
                case SpecialType.System_Int64:
                    ytype = cg.EmitConvertIntToLong(cg.Emit(right));  // bool|int -> long
                    if (ytype == cg.CoreTypes.PhpNumber)
                    {
                        // long / number : number
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Division_long_number)
                            .Expect(cg.CoreTypes.PhpNumber);
                    }
                    throw new NotImplementedException();
                default:
                    if (xtype == cg.CoreTypes.PhpNumber)
                    {
                        ytype = cg.EmitConvertIntToLong(cg.Emit(right));  // bool|int -> long
                        if (ytype == cg.CoreTypes.PhpNumber)
                        {
                            // nmumber / number : number
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Division_number_number)
                                .Expect(cg.CoreTypes.PhpNumber);
                        }
                    }

                    // x -> PhpValue
                    xtype = cg.EmitConvertToPhpValue(xtype, 0);
                    cg.EmitConvert(right, cg.CoreTypes.PhpValue);
                    ytype = cg.CoreTypes.PhpValue;

                    // value / value : number
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.Div_PhpValue_PhpValue);
            }
        }

        /// <summary>
        /// Emits <c>pow</c> operator.
        /// </summary>
        TypeSymbol EmitPow(CodeGenerator cg)
        {
            return EmitPow(cg, cg.Emit(Left), Left.TypeRefMask, Right);
        }

        internal static TypeSymbol EmitPow(CodeGenerator cg, TypeSymbol xtype, FlowAnalysis.TypeRefMask xtype_hint, BoundExpression right)
        {
            var il = cg.Builder;

            TypeSymbol ytype;
            xtype = cg.EmitConvertIntToLong(xtype);    // int|bool -> long

            switch (xtype.SpecialType)
            {
                case SpecialType.System_Int64:
                    ytype = cg.EmitConvertIntToLong(cg.Emit(right));    // int|bool -> long

                    if (ytype.SpecialType == SpecialType.System_Int64)
                    {
                        // i8 ** i8 : number
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Pow_long_long);
                    }
                    else if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // i8 ** r8 : r8
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Pow_long_double);
                    }
                    else if (ytype == cg.CoreTypes.PhpNumber)
                    {
                        // i8 ** number : number
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Pow_long_number);
                    }
                    // y -> PhpValue
                    cg.EmitConvert(ytype, right.TypeRefMask, cg.CoreTypes.PhpValue);
                    ytype = cg.CoreTypes.PhpValue;

                    // i8 ** value : number
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Pow_long_value);

                case SpecialType.System_Double:
                    ytype = cg.EmitConvertNumberToDouble(right);    // int|bool|long|number -> double

                    if (ytype.SpecialType == SpecialType.System_Double)
                    {
                        // r8 ** r8 : r8
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Pow_double_double);
                    }
                    // y -> PhpValue
                    cg.EmitConvert(ytype, right.TypeRefMask, cg.CoreTypes.PhpValue);
                    ytype = cg.CoreTypes.PhpValue;

                    // r8 ** value : r8
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Pow_double_value);

                default:
                    if (xtype == cg.CoreTypes.PhpNumber)
                    {
                        ytype = cg.EmitConvertIntToLong(cg.Emit(right));    // int|bool -> long
                        if (ytype == cg.CoreTypes.Double)
                        {
                            // number ** r8 : r8
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Pow_number_double);
                        }

                        if (ytype.SpecialType == SpecialType.System_Int64)
                        {
                            // y -> number
                            ytype = cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Create_Long);
                        }

                        if (ytype == cg.CoreTypes.PhpNumber)
                        {
                            // number ** number : number
                            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Pow_number_number);
                        }

                        // y -> PhpValue
                        ytype = cg.EmitConvertToPhpValue(ytype, right.TypeRefMask);

                        // number ** value : number
                        return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Pow_number_value);
                    }

                    // x -> PhpValue
                    xtype = cg.EmitConvertToPhpValue(xtype, xtype_hint);
                    cg.EmitConvert(right, cg.CoreTypes.PhpValue);
                    ytype = cg.CoreTypes.PhpValue;

                    // value ** value : number
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Pow_value_value);
            }
        }
    }

    partial class BoundUnaryEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            Debug.Assert(Access.IsRead || Access.IsNone);

            TypeSymbol returned_type;

            switch (this.Operation)
            {
                case Operations.AtSign:
                    // special arrangement
                    // Template:
                    //		context.DisableErrorReporting();
                    //		s;
                    //		context.EnableErrorReporting();
                    returned_type = cg.EmitWithDisabledErrorReporting(Operand);
                    break;

                case Operations.BitNegation:
                    //Template: "~x" Operators.BitNot(x)                                     
                    //codeGenerator.EmitBoxing(node.Expr.Emit(codeGenerator));
                    //il.Emit(OpCodes.Call, Methods.Operators.BitNot);
                    //returned_typecode = PhpTypeCode.Object;
                    //break;
                    throw new NotImplementedException();

                case Operations.Clone:
                    // Template: clone x        Operators.Clone(x,DTypeDesc,ScriptContext)
                    //codeGenerator.EmitBoxing(node.Expr.Emit(codeGenerator));
                    //codeGenerator.EmitLoadClassContext();
                    //codeGenerator.EmitLoadScriptContext();
                    //il.Emit(OpCodes.Call, Methods.Operators.Clone);
                    //returned_typecode = PhpTypeCode.Object;
                    //break;
                    throw new NotImplementedException();

                case Operations.LogicNegation:
                    //Template: !(bool)(x);                              
                    cg.EmitConvertToBool(this.Operand, true);
                    returned_type = cg.CoreTypes.Boolean;
                    break;

                case Operations.Minus:
                    //Template: "-x"
                    returned_type = EmitMinus(cg);
                    break;

                case Operations.Plus:
                    //Template: "+x"
                    returned_type = EmitPlus(cg);
                    break;

                case Operations.ObjectCast:
                    //Template: "(object)x"
                    cg.EmitConvert(this.Operand, cg.CoreTypes.Object);
                    returned_type = cg.CoreTypes.Object;
                    break;

                case Operations.Print:
                    cg.EmitEcho(this.Operand);

                    if (Access.IsRead)
                    {
                        // Always returns 1
                        cg.Builder.EmitLongConstant(1);
                        returned_type = cg.CoreTypes.Long;
                    }
                    else
                    {
                        // nobody reads the result anyway
                        returned_type = cg.CoreTypes.Void;
                    }
                    break;

                case Operations.BoolCast:
                    //Template: "(bool)x"
                    cg.EmitConvert(this.Operand, cg.CoreTypes.Boolean);
                    returned_type = cg.CoreTypes.Boolean;
                    break;

                case Operations.Int8Cast:
                case Operations.Int16Cast:
                case Operations.Int32Cast:
                case Operations.UInt8Cast:
                case Operations.UInt16Cast:

                case Operations.UInt64Cast:
                case Operations.UInt32Cast:
                case Operations.Int64Cast:

                    cg.EmitConvert(this.Operand, cg.CoreTypes.Long);
                    returned_type = cg.CoreTypes.Long;
                    break;

                case Operations.DecimalCast:
                case Operations.DoubleCast:
                case Operations.FloatCast:

                    cg.EmitConvert(this.Operand, cg.CoreTypes.Double);
                    returned_type = cg.CoreTypes.Double;
                    break;

                case Operations.UnicodeCast: // TODO
                case Operations.StringCast:
                    // (string)x
                    cg.EmitConvert(this.Operand, cg.CoreTypes.String);  // TODO: to String or PhpString ? to not corrupt single-byte string
                    return cg.CoreTypes.String;

                case Operations.BinaryCast:
                    //if ((returned_typecode = node.Expr.Emit(codeGenerator)) != PhpTypeCode.PhpBytes)
                    //{
                    //    codeGenerator.EmitBoxing(returned_typecode);
                    //    //codeGenerator.EmitLoadClassContext();
                    //    il.Emit(OpCodes.Call, Methods.Convert.ObjectToPhpBytes);
                    //    returned_typecode = PhpTypeCode.PhpBytes;
                    //}
                    //break;
                    throw new NotImplementedException();

                case Operations.ArrayCast:
                    //Template: "(array)x"
                    cg.EmitConvert(this.Operand, cg.CoreTypes.PhpArray);    // TODO: EmitArrayCast()
                    returned_type = cg.CoreTypes.PhpArray;
                    break;
                    
                case Operations.UnsetCast:
                    // Template: "(unset)x"  null

                    cg.EmitPop(cg.Emit(this.Operand));

                    if (this.Access.IsRead)
                    {
                        cg.Builder.EmitNullConstant();
                        returned_type = cg.CoreTypes.Object;
                    }
                    else
                    {
                        returned_type = cg.CoreTypes.Void;
                    }
                    break;

                default:
                    throw ExceptionUtilities.Unreachable;
            }

            switch (Access.Flags)
            {
                case AccessMask.Read:
                    Debug.Assert(returned_type.SpecialType != SpecialType.System_Void);
                    // do nothing
                    break;
                case AccessMask.None:
                    // pop operation's result value from stack
                    cg.EmitPop(returned_type);
                    returned_type = cg.CoreTypes.Void;
                    break;
                default:
                    throw ExceptionUtilities.UnexpectedValue(Access);
            }

            return returned_type;
        }

        TypeSymbol EmitMinus(CodeGenerator cg)
        {
            // Template: 0L - Operand

            var il = cg.Builder;
            var t = cg.Emit(this.Operand);

            switch (t.SpecialType)
            {
                case SpecialType.System_Double:
                    // -r8
                    il.EmitOpCode(ILOpCode.Neg);
                    return t;
                case SpecialType.System_Int32:
                    // -(i8)i4
                    il.EmitOpCode(ILOpCode.Conv_i8);    // i4 -> i8
                    il.EmitOpCode(ILOpCode.Neg);        // result will fit into long for sure
                    return cg.CoreTypes.Long;
                case SpecialType.System_Int64:
                    // PhpNumber.Minus(i8) : number
                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Negation_long)
                            .Expect(cg.CoreTypes.PhpNumber);
                default:
                    if (t != cg.CoreTypes.PhpNumber)
                    {
                        cg.EmitConvertToPhpNumber(t, 0);
                    }

                    return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpNumber.Negation)
                        .Expect(t);
            }
        }

        TypeSymbol EmitPlus(CodeGenerator cg)
        {
            // Template: 0L + Operand

            // convert value to a number

            var il = cg.Builder;
            var t = cg.Emit(this.Operand);

            switch (t.SpecialType)
            {
                case SpecialType.System_Double:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                    return t;
                case SpecialType.System_Boolean:
                    // (long)(int)bool
                    il.EmitOpCode(ILOpCode.Conv_i4);
                    il.EmitOpCode(ILOpCode.Conv_i8);
                    return cg.CoreTypes.Long;
                default:
                    if (t != cg.CoreTypes.PhpNumber)
                    {
                        cg.EmitConvertToPhpNumber(t, 0);
                    }

                    return cg.CoreTypes.PhpNumber;
            }
        }
    }

    partial class BoundLiteral
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            Debug.Assert(this.Access.IsRead || Access.IsNone);

            // do nothing
            if (this.Access.IsNone)
            {
                return cg.CoreTypes.Void;
            }

            // push value onto the evaluation stack

            Debug.Assert(ConstantObject.HasValue);
            return cg.EmitLoadConstant(ConstantObject.Value, this.Access.TargetType);
        }
    }

    partial class BoundReferenceExpression
    {
        /// <summary>
        /// Gets <see cref="IBoundReference"/> providing load and store operations.
        /// </summary>
        internal abstract IBoundReference BindPlace(CodeGenerator cg);

        internal abstract IPlace Place(ILBuilder il);

        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            Debug.Assert(this.Access.IsRead || this.Access.IsNone);

            if (Access.IsNone)
            {
                // do nothing
                return cg.CoreTypes.Void;
            }

            if (ConstantObject.HasValue)
            {
                return cg.EmitLoadConstant(ConstantObject.Value, this.Access.TargetType);
            }

            var place = this.BindPlace(cg);
            place.EmitLoadPrepare(cg);
            return place.EmitLoad(cg);
        }
    }

    partial class BoundVariableRef
    {
        internal override IBoundReference BindPlace(CodeGenerator cg) => this.Variable.BindPlace(cg.Builder, this.Access, this.TypeRefMask);

        internal override IPlace Place(ILBuilder il) => this.Variable.Place(il);
    }

    partial class BoundListEx : IBoundReference
    {
        internal override IBoundReference BindPlace(CodeGenerator cg) => this;

        internal override IPlace Place(ILBuilder il) => null;

        #region IBoundReference

        TypeSymbol IBoundReference.TypeOpt => null;

        void IBoundReference.EmitLoadPrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt)
        {
            throw new InvalidOperationException();
        }

        TypeSymbol IBoundReference.EmitLoad(CodeGenerator cg)
        {
            throw new InvalidOperationException();
        }

        void IBoundReference.EmitStorePrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt)
        {
            // nop
        }

        void IBoundReference.EmitStore(CodeGenerator cg, TypeSymbol valueType)
        {
            var rtype = cg.CoreTypes.IPhpArray;
            cg.EmitConvert(valueType, 0, rtype);

            var tmp = cg.GetTemporaryLocal(rtype);
            cg.Builder.EmitLocalStore(tmp);

            // NOTE: since PHP7, variables are assigned from left to right
            var vars = this.Variables;
            for (int i = 0; i < vars.Length; i++)
            {
                var target = vars[i];
                if (target == null)
                    continue;

                var boundtarget = target.BindPlace(cg);
                boundtarget.EmitStorePrepare(cg);

                // LOAD IPhpArray.GetItemValue(IntStringKey{i})
                cg.Builder.EmitLocalLoad(tmp);
                cg.EmitIntStringKey(i);
                var itemtype = cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.IPhpArray.GetItemValue_IntStringKey);

                // STORE vars[i]
                boundtarget.EmitStore(cg, itemtype);
            }

            //
            cg.ReturnTemporaryLocal(tmp);
        }

        #endregion
    }

    partial class BoundFieldRef
    {
        internal IBoundReference BoundReference { get; set; }

        IFieldSymbol FieldSymbolOpt => (BoundReference as BoundFieldPlace)?.Field;

        internal override IBoundReference BindPlace(CodeGenerator cg)
        {
            // TODO: constant bound reference
            Debug.Assert(BoundReference != null, "BoundReference was not set!");
            return BoundReference;
        }

        internal override IPlace Place(ILBuilder il)
        {
            var fldplace = BoundReference as BoundFieldPlace;
            if (fldplace != null)
            {
                Debug.Assert(fldplace.Field != null);

                var instanceplace = (fldplace.Instance as BoundReferenceExpression)?.Place(il);
                if (instanceplace != null || (fldplace.Instance == null && fldplace.Field.IsStatic))
                    return new FieldPlace(instanceplace, fldplace.Field);
            }

            return null;
        }
    }

    partial class BoundRoutineCall
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            return (TargetMethod != null)
                ? EmitDirectCall(cg, IsVirtualCall ? ILOpCode.Callvirt : ILOpCode.Call, TargetMethod)
                : EmitCallsiteCall(cg);
        }

        internal virtual TypeSymbol EmitDirectCall(CodeGenerator cg, ILOpCode opcode, MethodSymbol method)
        {
            // TODO: emit check the routine is declared
            // <ctx>.AssertFunctionDeclared

            var arguments = _arguments.Select(a => a.Value).ToImmutableArray();

            return cg.EmitCall(opcode, method, this.Instance, arguments);
        }

        protected virtual string CallsiteName => null;
        protected virtual BoundExpression RoutineNameExpr => null;
        protected virtual BoundExpression TypeNameExpr => null;
        protected virtual bool IsVirtualCall => true;
        
        internal virtual TypeSymbol EmitCallsiteCall(CodeGenerator cg)
        {
            // callsite

            var nameOpt = this.CallsiteName;
            var callsite = cg.Factory.StartCallSite("call_" + nameOpt);

            var callsiteargs = new List<TypeSymbol>(_arguments.Length);
            var return_type = this.Access.IsRead
                    ? this.Access.IsReadRef
                        ? cg.CoreTypes.PhpAlias.Symbol
                        : (this.Access.TargetType ?? cg.CoreTypes.PhpValue.Symbol)
                    : cg.CoreTypes.Void.Symbol;

            // callsite
            var fldPlace = callsite.Place;

            // LOAD callsite.Target
            callsite.EmitLoadTarget(cg.Builder);

            // LOAD callsite arguments

            // (callsite, [target], ctx, [name], ...)
            fldPlace.EmitLoad(cg.Builder);

            if (Instance != null)
            {
                callsiteargs.Add(cg.Emit(Instance));   // instance
            }
            else if (TypeNameExpr != null)
            {
                cg.EmitConvert(TypeNameExpr, cg.CoreTypes.String);
                callsiteargs.Add(cg.CoreTypes.String);   // type
            }

            callsiteargs.Add(cg.EmitLoadContext());     // ctx

            if (RoutineNameExpr != null)
            {
                callsiteargs.Add(cg.Emit(RoutineNameExpr));   // name
            }

            foreach (var a in _arguments)
            {
                callsiteargs.Add(cg.Emit(a.Value));
            }

            // Target()
            var functype = cg.Factory.GetCallSiteDelegateType(
                null, RefKind.None,
                callsiteargs.AsImmutable(),
                default(ImmutableArray<RefKind>),
                null,
                return_type);

            cg.EmitCall(ILOpCode.Callvirt, functype.DelegateInvokeMethod);

            // Create CallSite ...
            callsite.Construct(functype, cctor_cg => BuildCallsiteCreate(cctor_cg, return_type));

            //
            return return_type;
        }

        internal virtual void BuildCallsiteCreate(CodeGenerator cg, TypeSymbol returntype) { throw new InvalidOperationException(); }
    }

    partial class BoundGlobalFunctionCall
    {
        protected override string CallsiteName => _name.IsDirect ? _name.NameValue.ToString() : null;
        protected override BoundExpression RoutineNameExpr => _name.NameExpression;
        protected override bool IsVirtualCall => false;

        internal override TypeSymbol EmitCallsiteCall(CodeGenerator cg)
        {
            if (_name.IsDirect)
            {
                return base.EmitCallsiteCall(cg);
            }
            else
            {
                Debug.Assert(_name.NameExpression != null);

                // faster to emit PhpCallback.Invoke

                // NameExpression.AsCallback().Invoke(Context, PhpValue[])

                cg.EmitConvert(_name.NameExpression, cg.CoreTypes.IPhpCallable);    // (IPhpCallable)Name
                cg.EmitLoadContext();       // Context
                cg.Emit_NewArray(cg.CoreTypes.PhpValue, _arguments.Select(a => a.Value).ToArray()); // PhpValue[]

                return cg.EmitCall(ILOpCode.Callvirt, cg.CoreTypes.IPhpCallable.Symbol.LookupMember<MethodSymbol>("Invoke"));
            }
        }

        internal override void BuildCallsiteCreate(CodeGenerator cg, TypeSymbol returntype)
        {
            cg.Builder.EmitStringConstant(CallsiteName);
            cg.Builder.EmitStringConstant(_nameOpt.HasValue ? _nameOpt.Value.ToString() : null);
            cg.EmitLoadToken(returntype, null);
            cg.Builder.EmitIntConstant(0);
            cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Dynamic.CallBinderFactory_Function);
        }
    }

    partial class BoundInstanceFunctionCall
    {
        protected override string CallsiteName => _name.IsDirect ? _name.NameValue.ToString() : null;
        protected override BoundExpression RoutineNameExpr => _name.NameExpression;
        
        internal override void BuildCallsiteCreate(CodeGenerator cg, TypeSymbol returntype)
        {
            cg.Builder.EmitStringConstant(CallsiteName);        // name
            cg.EmitLoadToken(cg.CallerType, null);              // class context
            cg.EmitLoadToken(returntype, null);                 // return type
            cg.Builder.EmitIntConstant(0);                      // generic params count
            cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Dynamic.CallBinderFactory_InstanceFunction);
        }
    }

    partial class BoundStaticFunctionCall
    {
        protected override string CallsiteName => _name.IsDirect ? _name.NameValue.ToString() : null;
        protected override BoundExpression RoutineNameExpr => _name.NameExpression;
        protected override BoundExpression TypeNameExpr => _typeRef.TypeExpression;
        protected override bool IsVirtualCall => false;

        internal override void BuildCallsiteCreate(CodeGenerator cg, TypeSymbol returntype)
        {
            cg.EmitLoadToken(_typeRef.ResolvedType, null);      // type
            cg.Builder.EmitStringConstant(CallsiteName);        // name
            cg.EmitLoadToken(cg.CallerType, null);              // class context
            cg.EmitLoadToken(returntype, null);                 // return type
            cg.Builder.EmitIntConstant(0);                      // generic params count
            cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Dynamic.CallBinderFactory_StaticFunction);
        }
    }

    partial class BoundNewEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            if (TargetMethod != null)
            {
                return EmitDirectCall(cg, ILOpCode.Newobj, TargetMethod);
            }
            else
            {
                if (_typeref.ResolvedType != null)
                {
                    // context.Create<T>(params)
                    var create_t = cg.CoreTypes.Context.Symbol.GetMembers("Create")
                        .OfType<MethodSymbol>()
                        .Where(s => s.Arity == 1 && s.ParameterCount == 1 && s.Parameters[0].IsParams)
                        .Single()
                        .Construct(_typeref.ResolvedType);

                    cg.EmitLoadContext();   // Context
                    cg.Emit_NewArray(cg.CoreTypes.PhpValue, _arguments.Select(a => a.Value).ToArray());  // PhpValue[]

                    return cg.EmitCall(ILOpCode.Call, create_t);
                }
                else
                {
                    // ctx.Create(classname, params)
                    var create = cg.CoreTypes.Context.Symbol.GetMembers("Create")
                        .OfType<MethodSymbol>()
                        .Where(s => s.Arity == 0 && s.ParameterCount == 2 && s.Parameters[0].Type.PrimitiveTypeCode == Microsoft.Cci.PrimitiveTypeCode.String && s.Parameters[1].IsParams && ((ArrayTypeSymbol)s.Parameters[1].Type).ElementType == cg.CoreTypes.PhpValue)
                        .Single();

                    cg.EmitLoadContext();   // Context
                    _typeref.EmitClassName(cg);   // String
                    cg.Emit_NewArray(cg.CoreTypes.PhpValue, _arguments.Select(a => a.Value).ToArray());  // PhpValue[]

                    return cg.EmitCall(ILOpCode.Call, create);
                }
            }
        }
    }

    partial class BoundEcho
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            Debug.Assert(Access.IsNone);

            _arguments
                .Select(a => a.Value)
                .ForEach(cg.EmitEcho);

            return cg.CoreTypes.Void;
        }
    }

    partial class BoundConcatEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            // new PhpString()
            cg.EmitCall(ILOpCode.Newobj, cg.CoreMethods.Ctors.PhpString);

            // TODO: overload for 2, 3, 4 parameters directly

            // <PhpString>.Append(<expr>)
            foreach (var x in this.ArgumentsInSourceOrder)
            {
                var expr = x.Value;
                if (IsEmpty(expr))
                    continue;

                //
                cg.Builder.EmitOpCode(ILOpCode.Dup);    // PhpString

                var t = cg.Emit(expr);
                if (t == cg.CoreTypes.PhpString)
                {
                    cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.PhpString.Append_PhpString);
                }
                else
                {
                    // TODO: PhpValue -> PhpString (instead of String)

                    cg.EmitConvert(t, 0, cg.CoreTypes.String);
                    cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.PhpString.Append_String);
                }

                //
                cg.Builder.EmitOpCode(ILOpCode.Nop);
            }

            //
            return cg.CoreTypes.PhpString;
        }

        bool IsEmpty(BoundExpression x)
        {
            if (x.ConstantObject.HasValue)
            {
                var value = x.ConstantObject.Value;
                if (value == null)
                    return true;

                if (value is string && ((string)value).Length == 0)
                    return true;

                if (value is bool && ((bool)value) == false)
                    return true;
            }

            return false;
        }
    }

    partial class BoundIncludeEx
    {
        /// <summary>
        /// True for <c>include_once</c> or <c>require_once</c>.
        /// </summary>
        public bool IsOnceSemantic => this.InclusionType == Pchp.Syntax.InclusionTypes.IncludeOnce || this.InclusionType == Pchp.Syntax.InclusionTypes.RequireOnce;

        /// <summary>
        /// True for <c>require</c> or <c>require_once</c>.
        /// </summary>
        public bool IsRequireSemantic => this.InclusionType == Pchp.Syntax.InclusionTypes.Require || this.InclusionType == Pchp.Syntax.InclusionTypes.RequireOnce;

        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            TypeSymbol result;
            var isvoid = this.Access.IsNone;

            Debug.Assert(_arguments.Length == 1);
            Debug.Assert(_arguments[0].Value.Access.IsRead);
            Debug.Assert(Access.IsRead || Access.IsNone);

            var method = this.Target;
            if (method != null) // => IsResolved
            {
                // emit condition for include_once/require_once
                if (IsOnceSemantic)
                {
                    var tscript = method.ContainingType;

                    result = isvoid
                        ? cg.CoreTypes.Void.Symbol
                        : cg.DeclaringCompilation.GetTypeFromTypeRef(cg.Routine.TypeRefContext, this.TypeRefMask);

                    // Template: (<ctx>.CheckIncludeOnce<TScript>()) ? <Main>() : TRUE
                    // Template<isvoid>: if (<ctx>.CheckIncludeOnce<TScript>()) <Main>()
                    var falseLabel = new object();
                    var endLabel = new object();

                    cg.EmitLoadContext();
                    cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.Context.CheckIncludeOnce_TScript.Symbol.Construct(tscript));

                    cg.Builder.EmitBranch(ILOpCode.Brfalse, falseLabel);

                    // ? (PhpValue)<Main>(...)
                    cg.EmitCallMain(method);
                    if (isvoid)
                    {
                        cg.EmitPop(method.ReturnType);
                    }
                    else
                    {
                        cg.EmitConvert(method.ReturnType, 0, result);
                    }
                    cg.Builder.EmitBranch(ILOpCode.Br, endLabel);

                    if (!isvoid)
                    {
                        cg.Builder.AdjustStack(-1); // workarounds assert in ILBuilder.MarkLabel, we're doing something wrong with ILBuilder
                    }

                    // : PhpValue.Create(true)
                    cg.Builder.MarkLabel(falseLabel);
                    if (!isvoid)
                    {
                        cg.Builder.EmitBoolConstant(true);
                        cg.EmitConvert(cg.CoreTypes.Boolean, 0, result);
                    }

                    //
                    cg.Builder.MarkLabel(endLabel);
                }
                else
                {
                    // <Main>
                    result = cg.EmitCallMain(method);
                }
            }
            else
            {
                Debug.Assert(cg.LocalsPlaceOpt != null);

                // Template: <ctx>.Include(dir, path, locals, @this, bool once = false, bool throwOnError = false)
                cg.EmitLoadContext();
                cg.Builder.EmitStringConstant(cg.Routine.ContainingFile.DirectoryRelativePath);
                cg.EmitConvert(_arguments[0].Value, cg.CoreTypes.String);
                cg.LocalsPlaceOpt.EmitLoad(cg.Builder); // scope of local variables, corresponds to $GLOBALS in global scope.
                cg.EmitThisOrNull();    // $this
                cg.Builder.EmitBoolConstant(IsOnceSemantic);
                cg.Builder.EmitBoolConstant(IsRequireSemantic);
                return cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.Context.Include_string_string_PhpArray_object_bool_bool);
            }

            //
            return result;
        }
    }

    partial class BoundExitEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            MethodSymbol ctorsymbol;

            if (_arguments.Length == 0)
            {
                // <ctx>.Exit();
                ctorsymbol = cg.CoreMethods.Ctors.ScriptDiedException;
            }
            else
            {
                // LOAD <status>
                var t = cg.Emit(_arguments[0].Value);

                switch (t.SpecialType)
                {
                    case SpecialType.System_Int32:
                        cg.Builder.EmitOpCode(ILOpCode.Conv_i8);    // i4 -> i8
                        goto case SpecialType.System_Int64;

                    case SpecialType.System_Int64:
                        ctorsymbol = cg.CoreMethods.Ctors.ScriptDiedException_Long;
                        break;

                    default:
                        cg.EmitConvertToPhpValue(t, 0);
                        ctorsymbol = cg.CoreMethods.Ctors.ScriptDiedException_PhpValue;
                        break;
                }
            }

            //
            cg.EmitCall(ILOpCode.Newobj, ctorsymbol);
            cg.Builder.EmitThrow(false);

            //
            return cg.CoreTypes.Void;
        }
    }

    partial class BoundAssignEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            var target_place = this.Target.BindPlace(cg);
            Debug.Assert(target_place != null);
            Debug.Assert(target_place.TypeOpt == null || target_place.TypeOpt.SpecialType != SpecialType.System_Void);

            // T tmp; // in case access is Read
            var t_value = target_place.TypeOpt;
            if (t_value == cg.CoreTypes.PhpAlias || t_value == cg.CoreTypes.PhpValue)
                t_value = null; // no inplace conversion

            LocalDefinition tmp = null;

            // <target> = <value>
            target_place.EmitStorePrepare(cg);

            // TODO: load value & dereference eventually
            if (t_value != null) cg.EmitConvert(this.Value, t_value);   // TODO: do not convert here yet
            else t_value = cg.Emit(this.Value);

            switch (this.Access.Flags)
            {
                case AccessMask.Read:
                    tmp = cg.GetTemporaryLocal(t_value, false);
                    cg.Builder.EmitOpCode(ILOpCode.Dup);
                    cg.Builder.EmitLocalStore(tmp);
                    break;
                case AccessMask.None:
                    break;
                default:
                    throw ExceptionUtilities.UnexpectedValue(this.Access);
            }

            target_place.EmitStore(cg, t_value);

            //
            switch (this.Access.Flags)
            {
                case AccessMask.None:
                    t_value = cg.CoreTypes.Void;
                    break;
                case AccessMask.Read:
                    cg.Builder.EmitLocalLoad(tmp);
                    break;
                default:
                    throw ExceptionUtilities.UnexpectedValue(this.Access);
            }

            if (tmp != null)
            {
                cg.ReturnTemporaryLocal(tmp);
            }

            //
            return t_value;
        }
    }

    partial class BoundCompoundAssignEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            Debug.Assert(Access.IsRead || Access.IsNone);

            // target X= value;

            var target_place = this.Target.BindPlace(cg);
            Debug.Assert(target_place != null);
            Debug.Assert(target_place.TypeOpt == null || target_place.TypeOpt.SpecialType != SpecialType.System_Void);

            // helper class maintaining reference to already evaluated instance of the eventual chain
            using (var instance_holder = new InstanceCacheHolder())
            {
                // <target> = <target> X <value>
                target_place.EmitStorePrepare(cg, instance_holder);

                //
                target_place.EmitLoadPrepare(cg, instance_holder);
            }

            var xtype = target_place.EmitLoad(cg);  // type of left value operand

            TypeSymbol result_type;

            switch (this.Operation)
            {
                case Operations.AssignAdd:
                    result_type = BoundBinaryEx.EmitAdd(cg, xtype, Value, target_place.TypeOpt);
                    break;
                //case Operations.AssignAnd:
                //    binaryop = Operations.And;
                //    break;
                case Operations.AssignAppend:
                    result_type = EmitAppend(cg, xtype, Value);
                    break;
                ////case Operations.AssignPrepend:
                ////    break;
                case Operations.AssignDiv:
                    result_type = BoundBinaryEx.EmitDiv(cg, xtype, Value, target_place.TypeOpt);
                    break;
                //case Operations.AssignMod:
                //    binaryop = Operations.Mod;
                //    break;
                case Operations.AssignMul:
                    result_type = BoundBinaryEx.EmitMul(cg, xtype, Value, target_place.TypeOpt);
                    break;
                //case Operations.AssignOr:
                //    binaryop = Operations.Or;
                //    break;
                case Operations.AssignPow:
                    result_type = BoundBinaryEx.EmitPow(cg, xtype, /*this.Target.TypeRefMask*/0, Value);
                    break;
                //case Operations.AssignShiftLeft:
                //    binaryop = Operations.ShiftLeft;
                //    break;
                //case Operations.AssignShiftRight:
                //    binaryop = Operations.ShiftRight;
                //    break;
                case Operations.AssignSub:
                    result_type = BoundBinaryEx.EmitSub(cg, xtype, Value, target_place.TypeOpt);
                    break;
                //case Operations.AssignXor:
                //    binaryop = Operations.Xor;
                //    break;
                default:
                    throw ExceptionUtilities.UnexpectedValue(this.Operation);
            }

            LocalDefinition tmp = null;

            switch (this.Access.Flags)
            {
                case AccessMask.Read:
                    tmp = cg.GetTemporaryLocal(result_type, false);
                    cg.Builder.EmitOpCode(ILOpCode.Dup);
                    cg.Builder.EmitLocalStore(tmp);
                    break;
                case AccessMask.None:
                    break;
                default:
                    throw ExceptionUtilities.UnexpectedValue(this.Access);
            }

            target_place.EmitStore(cg, result_type);

            //
            switch (this.Access.Flags)
            {
                case AccessMask.None:
                    return cg.CoreTypes.Void;
                case AccessMask.Read:
                    Debug.Assert(tmp != null);
                    cg.Builder.EmitLoad(tmp);
                    cg.ReturnTemporaryLocal(tmp);
                    return result_type;
                default:
                    throw ExceptionUtilities.UnexpectedValue(this.Access);
            }
        }

        static TypeSymbol EmitAppend(CodeGenerator cg, TypeSymbol xtype, BoundExpression y)
        {
            if (xtype == cg.CoreTypes.PhpString)
            {
                // x.Append(y); return x;
                cg.Builder.EmitOpCode(ILOpCode.Dup);

                cg.EmitConvert(y, cg.CoreTypes.String);
                cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.PhpString.Append_String);

                //
                return xtype;
            }
            else
            {
                // concat(x, y)
                cg.EmitConvert(xtype, 0, cg.CoreTypes.String);
                cg.EmitConvert(y, cg.CoreTypes.String);

                return cg.EmitCall(ILOpCode.Newobj, cg.CoreMethods.Ctors.PhpString_string_string);
            }
        }
    }

    partial class BoundIncDecEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            Debug.Assert(this.Access.IsNone || Access.IsRead);
            Debug.Assert(!this.Access.IsReadRef);
            Debug.Assert(!this.Access.IsWrite);
            Debug.Assert(this.Target.Access.IsRead && this.Target.Access.IsWrite);
            Debug.Assert(this.Value.Access.IsRead);

            Debug.Assert(this.Value is BoundLiteral);

            if (this.UsesOperatorMethod)
            {
                throw new NotImplementedException();
            }

            TypeSymbol result_type = cg.CoreTypes.Void;
            LocalDefinition postfix_temp = null;

            var read = this.Access.IsRead;

            var target_place = this.Target.BindPlace(cg);
            Debug.Assert(target_place != null);

            using (var instance_holder = new InstanceCacheHolder())
            {

                // prepare target for store operation
                target_place.EmitStorePrepare(cg, instance_holder);

                // load target value
                target_place.EmitLoadPrepare(cg, instance_holder);
            }

            var target_load_type = target_place.EmitLoad(cg);

            TypeSymbol op_type;

            if (read && IsPostfix)
            {
                // store original value of target
                // <temp> = TARGET
                postfix_temp = cg.GetTemporaryLocal(target_load_type);
                cg.EmitOpCode(ILOpCode.Dup);
                cg.Builder.EmitLocalStore(postfix_temp);
            }

            if (IsIncrement)
            {
                op_type = BoundBinaryEx.EmitAdd(cg, target_load_type, this.Value, target_place.TypeOpt);
            }
            else
            {
                Debug.Assert(IsDecrement);
                op_type = BoundBinaryEx.EmitSub(cg, target_load_type, this.Value, target_place.TypeOpt);
            }

            if (read)
            {
                if (IsPostfix)
                {
                    // READ <temp>
                    cg.Builder.EmitLocalLoad(postfix_temp);
                    result_type = target_load_type;

                    //
                    cg.ReturnTemporaryLocal(postfix_temp);
                    postfix_temp = null;
                }
                else
                {
                    // dup resulting value
                    // READ (++TARGET OR --TARGET)
                    cg.Builder.EmitOpCode(ILOpCode.Dup);
                    result_type = op_type;
                }
            }

            //
            target_place.EmitStore(cg, op_type);

            Debug.Assert(postfix_temp == null);
            Debug.Assert(!read || result_type.SpecialType != SpecialType.System_Void);

            //
            return result_type;
        }

        bool IsPostfix => this.IncrementKind == UnaryOperationKind.OperatorPostfixIncrement || this.IncrementKind == UnaryOperationKind.OperatorPostfixDecrement;
        bool IsIncrement => this.IncrementKind == UnaryOperationKind.OperatorPostfixIncrement || this.IncrementKind == UnaryOperationKind.OperatorPrefixIncrement;
        bool IsDecrement => this.IncrementKind == UnaryOperationKind.OperatorPostfixDecrement || this.IncrementKind == UnaryOperationKind.OperatorPrefixDecrement;
    }

    partial class BoundConditionalEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            var result_type = cg.DeclaringCompilation.GetTypeFromTypeRef(cg.Routine, this.TypeRefMask);

            if (this.IfTrue != null)
            {
                object trueLbl = new object();
                object endLbl = new object();

                // Cond ? True : False
                cg.EmitConvert(this.Condition, cg.CoreTypes.Boolean);   // i4
                cg.Builder.EmitBranch(ILOpCode.Brtrue, trueLbl);

                // false:
                cg.EmitConvert(this.IfFalse, result_type);
                cg.Builder.EmitBranch(ILOpCode.Br, endLbl);
                cg.Builder.AdjustStack(-1); // workarounds assert in ILBuilder.MarkLabel, we're doing something wrong with ILBuilder
                // trueLbl:
                cg.Builder.MarkLabel(trueLbl);
                cg.EmitConvert(this.IfTrue, result_type);

                // endLbl:
                cg.Builder.MarkLabel(endLbl);
            }
            else
            {
                object trueLbl = new object();
                object endLbl = new object();

                // Cond ?: False

                // <stack> = <cond_var> = Cond
                var cond_type = cg.Emit(this.Condition);
                var cond_var = cg.GetTemporaryLocal(cond_type);
                cg.Builder.EmitOpCode(ILOpCode.Dup);
                cg.Builder.EmitLocalStore(cond_var);

                cg.EmitConvertToBool(cond_type, this.Condition.TypeRefMask);
                cg.Builder.EmitBranch(ILOpCode.Brtrue, trueLbl);

                // false:
                cg.EmitConvert(this.IfFalse, result_type);
                cg.Builder.EmitBranch(ILOpCode.Br, endLbl);
                cg.Builder.AdjustStack(-1); // workarounds assert in ILBuilder.MarkLabel, we're doing something wrong with ILBuilder

                // trueLbl:
                cg.Builder.MarkLabel(trueLbl);
                cg.Builder.EmitLocalLoad(cond_var);
                cg.EmitConvert(cond_type, this.Condition.TypeRefMask, result_type);

                // endLbl:
                cg.Builder.MarkLabel(endLbl);

                //
                cg.ReturnTemporaryLocal(cond_var);
            }

            //
            if (Access.IsNone)
            {
                cg.EmitPop(result_type);
                result_type = cg.CoreTypes.Void;
            }

            //
            return result_type;
        }
    }

    partial class BoundArrayEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            // new PhpArray(count)
            cg.Builder.EmitIntConstant(_items.Length);
            var result = cg.EmitCall(ILOpCode.Newobj, cg.CoreMethods.Ctors.PhpArray_int)
                .Expect(cg.CoreTypes.PhpArray);

            foreach (var x in _items)
            {
                // <PhpArray>
                cg.Builder.EmitOpCode(ILOpCode.Dup);

                // key
                if (x.Key != null)
                {
                    cg.EmitIntStringKey(x.Key);
                }

                // value | alias
                Debug.Assert(x.Value != null);

                var byref = x.Value.Access.IsReadRef;
                var valuetype = byref ? cg.CoreTypes.PhpAlias : cg.CoreTypes.PhpValue;
                cg.EmitConvert(x.Value, valuetype);

                if (x.Key != null)
                {
                    if (byref)  // .SetItemAlias( key, PhpAlias )
                        cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpArray.SetItemAlias_IntStringKey_PhpAlias);
                    else   // .SetItemValue( key, PhpValue )
                        cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpArray.SetItemValue_IntStringKey_PhpValue);
                }
                else
                {
                    if (byref)  // PhpValue.Create( PhpAlias )
                        cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.Create_PhpAlias);

                    // .AddValue( PhpValue )
                    cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpArray.AddValue_PhpValue);
                }
            }

            //
            return result;
        }
    }

    partial class BoundArrayItemEx : IBoundReference
    {
        internal override IBoundReference BindPlace(CodeGenerator cg)
        {
            this.Array.Access = this.Array.Access.WithRead(cg.CoreTypes.IPhpArray);
            _type = Access.IsReadRef ? cg.CoreTypes.PhpAlias : cg.CoreTypes.PhpValue;
            return this;
        }

        internal override IPlace Place(ILBuilder il) => null;

        #region IBoundReference

        TypeSymbol IBoundReference.TypeOpt => _type;
        TypeSymbol _type;

        void EmitArrayPrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt)
        {
            InstanceCacheHolder.EmitInstance(instanceOpt, cg, Array);

            if (Array.ResultType.IsOfType(cg.CoreTypes.IPhpArray))
            {
                // ok
            }
            else if (Array.ResultType == cg.CoreTypes.PhpValue)
            {
                // Convert.AsArray()
                cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.AsArray_PhpValue);
            }
            else
            {
                throw new NotImplementedException();    // TODO: emit convert as PhpArray
            }
        }

        void IBoundReference.EmitLoadPrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt)
        {
            // Template: array[index]

            EmitArrayPrepare(cg, instanceOpt);

            if (this.Index == null)
                throw new ArgumentException();

            cg.EmitIntStringKey(this.Index);    // TODO: save Index into InstanceCacheHolder
        }

        TypeSymbol IBoundReference.EmitLoad(CodeGenerator cg)
        {
            // Template: array[index]

            // OPTIMIZATION: .call instead of .callvirt in case of known sealed array type (e.g. PhpArray instead of IPhpArray)

            if (Access.EnsureObject)
            {
                // <array>.EnsureItemObject(<key>)
                return cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.IPhpArray.EnsureItemObject_IntStringKey);
            }
            else if (Access.EnsureArray)
            {
                return cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.IPhpArray.EnsureItemArray_IntStringKey);
            }
            else if (Access.IsReadRef)
            {
                return cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.IPhpArray.EnsureItemAlias_IntStringKey);
            }
            else
            {
                Debug.Assert(Access.IsRead);
                return cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.IPhpArray.GetItemValue_IntStringKey);
            }
        }

        void IBoundReference.EmitStorePrepare(CodeGenerator cg, InstanceCacheHolder instanceOpt)
        {
            // Template: array[index]

            EmitArrayPrepare(cg, instanceOpt);

            if (this.Index != null)
            {
                cg.EmitIntStringKey(this.Index);    // TODO: save Index into InstanceCacheHolder
            }
        }

        void IBoundReference.EmitStore(CodeGenerator cg, TypeSymbol valueType)
        {
            // Template: array[index]

            if (Access.IsWriteRef)
            {
                // PhpAlias
                if (valueType != cg.CoreTypes.PhpAlias)
                {
                    cg.EmitConvertToPhpValue(valueType, 0);
                    cg.Emit_PhpValue_MakeAlias();
                }

                // .SetItemAlias(key, alias) or .AddValue(PhpValue.Create(alias))
                if (this.Index != null)
                {
                    cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.IPhpArray.SetItemAlias_IntStringKey_PhpAlias);
                }
                else
                {
                    cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.Create_PhpAlias);
                    cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.IPhpArray.AddValue_PhpValue);
                }
            }
            else if (Access.IsUnset)
            {
                if (this.Index == null)
                    throw new InvalidOperationException();

                // .RemoveKey(key)
                cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.IPhpArray.RemoveKey_IntStringKey);
            }
            else
            {
                Debug.Assert(Access.IsWrite);

                cg.EmitConvertToPhpValue(valueType, 0);

                // .SetItemValue(key, value) or .AddValue(value)
                if (this.Index != null)
                {
                    cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.IPhpArray.SetItemValue_IntStringKey_PhpValue);
                }
                else
                {
                    cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.IPhpArray.AddValue_PhpValue);
                }
            }
        }

        #endregion
    }

    partial class BoundInstanceOfEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            Debug.Assert(Access.IsRead || Access.IsNone);

            var type = cg.Emit(Operand);

            //
            if (Access.IsNone)
            {
                cg.EmitPop(type);
                return cg.CoreTypes.Void;
            }

            // dereference
            if (type == cg.CoreTypes.PhpAlias)
            {
                // <alias>.Value.AsObject()
                cg.Emit_PhpAlias_GetValueRef();
                type = cg.EmitCall(ILOpCode.Call, cg.CoreMethods.PhpValue.AsObject);
            }

            // PhpValue -> object
            if (type == cg.CoreTypes.PhpValue)
            {
                // Template: Operators.AsObject(value) is T
                type = cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.AsObject_PhpValue);
            }

            //
            if (IsTypeResolved != null)
            {
                if (type.IsReferenceType && type != cg.CoreTypes.PhpArray && type != cg.CoreTypes.PhpString)
                {
                    // Template: value is T : object
                    cg.Builder.EmitOpCode(ILOpCode.Isinst);
                    cg.EmitSymbolToken(IsTypeResolved, null);

                    // object != null
                    cg.Builder.EmitNullConstant(); // .ldnull
                    cg.Builder.EmitOpCode(ILOpCode.Cgt_un); // .cgt.un
                }
                else
                {
                    cg.EmitPop(type);   // Operand is never an object instance

                    // FALSE
                    cg.Builder.EmitBoolConstant(false);
                }

                //
                return cg.CoreTypes.Boolean;
            }
            else
            {
                // Template: Operators.IsA(value, type);
            }

            throw new NotImplementedException();
        }
    }

    partial class BoundPseudoConst
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            switch (this.Type)
            {
                case PseudoConstUse.Types.File:

                    // <ctx>.FilePath<TScript>()
                    cg.EmitLoadContext();
                    return cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.Context.ScriptPath_TScript.Symbol.Construct(cg.Routine.ContainingFile))
                        .Expect(SpecialType.System_String);

                default:
                    throw new NotImplementedException(Type.ToString());
            }
        }
    }

    partial class BoundGlobalConst
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            Debug.Assert(!Access.IsWrite);

            if (this.Access.IsNone)
            {
                return cg.CoreTypes.Void;
            }

            if (this.ConstantObject.HasValue)
            {
                return cg.EmitLoadConstant(this.ConstantObject.Value, this.Access.TargetType);
            }

            if (_boundExpressionOpt != null)
            {
                _boundExpressionOpt.EmitLoadPrepare(cg);
                return _boundExpressionOpt.EmitLoad(cg);
            }

            var idxfield = ((IWithSynthesized)cg.Module.ScriptType)
                .GetOrCreateSynthesizedField(cg.CoreTypes.Int32, $"c<{this.Name}>idx", Accessibility.Internal, true, false);

            // <ctx>.GetConstant(<name>, ref <Index of constant>)
            cg.EmitLoadContext();
            cg.Builder.EmitStringConstant(this.Name);
            cg.EmitFieldAddress(idxfield);
            return cg.EmitCall(ILOpCode.Callvirt, cg.CoreMethods.Context.GetConstant_string_int32)
                .Expect(cg.CoreTypes.PhpValue);
        }
    }

    partial class BoundIsEmptyEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            cg.EmitConvert(this.Variable, cg.CoreTypes.PhpValue);
            return cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.IsEmpty_PhpValue)
                .Expect(SpecialType.System_Boolean);
        }
    }

    partial class BoundIsSetEx
    {
        internal override TypeSymbol Emit(CodeGenerator cg)
        {
            var end_label = new object();

            var vars = this.VarReferences;
            for (int i = 0; i < vars.Length; i++)
            {
                if (i > 0)
                {
                    cg.Builder.EmitOpCode(ILOpCode.Pop);
                }

                var t = cg.Emit(vars[i]);

                // t.IsSet
                if (t == cg.CoreTypes.PhpValue)
                {
                    // IsSet(value)
                    cg.EmitCall(ILOpCode.Call, cg.CoreMethods.Operators.IsSet_PhpValue);
                }
                else if (t.IsReferenceType)
                {
                    // object != null
                    cg.Builder.EmitNullConstant(); // .ldnull
                    cg.Builder.EmitOpCode(ILOpCode.Cgt_un); // .cgt.un
                }
                else
                {
                    // value type => true
                    cg.EmitPop(t);
                    cg.Builder.EmitBoolConstant(true);
                }

                if (i + 1 < vars.Length)
                {
                    // if (result == false) goto end_label;
                    cg.Builder.EmitOpCode(ILOpCode.Dup);
                    cg.Builder.EmitBranch(ILOpCode.Brfalse, end_label);
                }
            }

            //
            cg.Builder.MarkLabel(end_label);

            //
            return cg.CoreTypes.Boolean;
        }
    }
}
