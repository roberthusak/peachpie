﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Pchp.Core.Dynamic
{
    /// <summary>
    /// Helper methods for converting expressions.
    /// </summary>
    internal static class ConvertExpression
    {
        #region Bind

        /// <summary>
        /// Creates expression that converts <paramref name="arg"/> to <paramref name="target"/> type.
        /// </summary>
        /// <param name="arg">Source expression to be converted.</param>
        /// <param name="target">Target type.</param>
        /// <param name="ctx">Expression with current context.</param>
        /// <returns>Expression converting <paramref name="arg"/> to <paramref name="target"/> type.</returns>
        public static Expression Bind(Expression arg, Type target, Expression ctx = null)
        {
            if (arg.Type == target)
                return arg;

            // dereference
            if (arg.Type == typeof(PhpAlias))
            {
                arg = Expression.PropertyOrField(arg, "Value");

                if (target == typeof(PhpValue))
                    return arg;
            }

            if (ctx == null)
            {
                //Debug.Assert(false, "Provide context Expression");
                ctx = Expression.Constant(null, typeof(Context));
            }

            //
            if (target == typeof(long)) return BindToLong(arg);
            if (target == typeof(int)) return Expression.Convert(BindToLong(arg), target);
            if (target == typeof(double)) return BindToDouble(arg);
            if (target == typeof(string)) return BindToString(arg, ctx);
            if (target == typeof(bool)) return BindToBool(arg);
            if (target == typeof(PhpNumber)) return BindToNumber(arg);
            if (target == typeof(PhpValue)) return BindToValue(arg);
            if (target == typeof(void)) return BindToVoid(arg);
            if (target == typeof(object)) return BindToClass(arg);
            if (target == typeof(PhpArray)) return BindAsArray(arg);
            if (target == typeof(IPhpArray)) return BindAsArray(arg);   // TODO
            if (target == typeof(IPhpCallable)) return BindAsCallable(arg);
            if (target == typeof(PhpString)) return BindToPhpString(arg, ctx);

            var target_type = target.GetTypeInfo();

            if (target_type.IsEnum) return Expression.Convert(BindToLong(arg), target);
            if (target_type.IsValueType == false) { } // TODO

            //
            throw new NotImplementedException(target.ToString());
        }

        private static Expression BindToLong(Expression expr)
        {
            var source = expr.Type;

            if (source == typeof(int)) return Expression.Convert(expr, typeof(long));
            if (source == typeof(long)) return expr;    // unreachable
            if (source == typeof(double)) return Expression.Convert(expr, typeof(long));
            if (source == typeof(PhpNumber)) return Expression.Call(expr, typeof(PhpNumber).GetMethod("ToLong", Cache.Types.Empty));
            if (source == typeof(PhpArray)) return Expression.Call(expr, typeof(PhpArray).GetMethod("ToLong", Cache.Types.Empty));
            if (source == typeof(void)) return VoidAsConstant(expr, 0L, typeof(long));

            // TODO: following conversions may fail, we should report it failed and throw an error
            if (source == typeof(PhpValue)) return Expression.Call(expr, typeof(PhpValue).GetMethod("ToLong", Cache.Types.Empty));

            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindToDouble(Expression expr)
        {
            var source = expr.Type;

            if (source == typeof(int)) return Expression.Convert(expr, typeof(double));
            if (source == typeof(long)) return Expression.Convert(expr, typeof(double));
            if (source == typeof(PhpNumber)) return Expression.Call(expr, typeof(PhpNumber).GetMethod("ToDouble", Cache.Types.Empty));
            if (source == typeof(PhpArray)) return Expression.Call(expr, typeof(PhpArray).GetMethod("ToDouble", Cache.Types.Empty));
            if (source == typeof(void)) return VoidAsConstant(expr, 0.0, typeof(double));
            if (source == typeof(double)) return expr;

            // TODO: following conversions may fail, we should report it failed and throw an error
            if (source == typeof(PhpValue)) return Expression.Call(expr, typeof(PhpValue).GetMethod("ToDouble", Cache.Types.Empty));

            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindToBool(Expression expr)
        {
            var source = expr.Type;

            if (source == typeof(int)) return Expression.Convert(expr, typeof(bool));
            if (source == typeof(long)) return Expression.Convert(expr, typeof(bool));
            if (source == typeof(PhpNumber)) return Expression.Call(expr, typeof(PhpNumber).GetMethod("ToBoolean", Cache.Types.Empty));
            if (source == typeof(PhpArray)) return Expression.Call(expr, typeof(PhpArray).GetMethod("ToBoolean", Cache.Types.Empty));
            if (source == typeof(PhpValue)) return Expression.Call(expr, typeof(PhpValue).GetMethod("ToBoolean", Cache.Types.Empty));
            if (source == typeof(void)) return VoidAsConstant(expr, false, typeof(bool));
            if (source == typeof(bool)) return expr;

            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindToString(Expression expr, Expression ctx)
        {
            var source = expr.Type;

            if (source == typeof(int) ||
                source == typeof(long) ||
                source == typeof(double))   // TODO: ToString_Double_Context
                return Expression.Call(expr, Cache.Object.ToString);

            if (source == typeof(string))
                return expr;

            if (source == typeof(PhpValue))
                return Expression.Call(expr, Cache.Operators.PhpValue_ToString_Context, ctx);

            if (source == typeof(void))
                return VoidAsConstant(expr, string.Empty, typeof(string));

            if (source == typeof(PhpNumber))
                return Expression.Call(expr, Cache.Operators.PhpNumber_ToString_Context, ctx);

            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindToPhpString(Expression expr, Expression ctx)
        {
            var source = expr.Type;

            // string -> PhpString
            if (source == typeof(int) ||
                source == typeof(long) ||
                source == typeof(double))   // TODO: ToString_Double_Context
            {
                expr = Expression.Call(expr, Cache.Object.ToString);
                source = expr.Type;
            }

            if (source == typeof(PhpValue))
            {
                expr = Expression.Call(expr, Cache.Operators.PhpValue_ToString_Context, ctx);   // TODO: PhpValue.AsPhpString(ctx)
                source = expr.Type;
            }

            if (source == typeof(string)) return Expression.New(Cache.PhpString.ctor_String, expr);        // new PhpString(string)
            if (source == typeof(byte[])) return Expression.New(Cache.PhpString.ctor_ByteArray, expr);     // new PhpString(byte[])

            //
            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindToNumber(Expression expr)
        {
            var source = expr.Type;

            //
            if (source == typeof(int))
            {
                source = typeof(long);
                expr = Expression.Convert(expr, typeof(long));
            }

            //
            if (source == typeof(long)) return Expression.Call(typeof(PhpNumber).GetMethod("Create", Cache.Types.Long), expr);
            if (source == typeof(double)) return Expression.Call(typeof(PhpNumber).GetMethod("Create", Cache.Types.Double), expr);
            if (source == typeof(void)) return VoidAsConstant(expr, PhpNumber.Default, typeof(PhpNumber));
            if (source == typeof(PhpNumber)) return expr;
            if (source == typeof(PhpValue)) return Expression.Convert(expr, typeof(PhpNumber));

            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindToValue(Expression expr)
        {
            var source = expr.Type;

            //
            if (source == typeof(bool)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.Bool), expr);
            if (source == typeof(int)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.Int), expr);
            if (source == typeof(long)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.Long), expr);
            if (source == typeof(double)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.Double), expr);
            if (source == typeof(string)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.String), expr);
            if (source == typeof(PhpString)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.PhpString), expr);
            if (source == typeof(PhpNumber)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.PhpNumber), expr);
            if (source == typeof(PhpValue)) return expr;
            if (source == typeof(PhpArray)) return Expression.Call(typeof(PhpValue).GetMethod("Create", Cache.Types.PhpArray), expr);

            if (source.GetTypeInfo().IsValueType)
            {
                if (source == typeof(void)) return VoidAsConstant(expr, PhpValue.Void, Cache.Types.PhpValue[0]);

                throw new NotImplementedException(source.FullName);
            }
            else
            {
                // TODO: FromClr
                return Expression.Call(typeof(PhpValue).GetMethod("FromClass", Cache.Types.Object), expr);
            }
        }

        private static Expression BindToClass(Expression expr)
        {
            var source = expr.Type;

            if (source == typeof(PhpValue)) return Expression.Call(expr, Cache.Operators.PhpValue_ToClass);
            if (source == typeof(PhpArray)) return Expression.Call(expr, Cache.Operators.PhpArray_ToClass);
            if (source == typeof(PhpNumber)) return Expression.Call(expr, typeof(PhpNumber).GetMethod("ToClass", Cache.Types.Empty));

            if (!source.GetTypeInfo().IsValueType) return expr;

            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindAsArray(Expression expr)
        {
            var source = expr.Type;

            if (source == typeof(PhpArray)) return expr;
            if (source == typeof(PhpValue)) return Expression.Call(expr, Cache.Operators.PhpValue_AsArray);

            throw new NotImplementedException(source.FullName);
        }

        private static Expression BindAsCallable(Expression expr)
        {
            var source = expr.Type;

            if (typeof(IPhpCallable).GetTypeInfo().IsAssignableFrom(source.GetTypeInfo())) return expr;

            return Expression.Call(BindToValue(expr), Cache.Operators.PhpValue_AsCallable);
        }

        private static Expression BindToVoid(Expression expr)
        {
            var source = expr.Type;

            if (source != typeof(void))
            {
                return Expression.Block(typeof(void), expr);
            }
            else
            {
                return expr;
            }
        }

        private static Expression VoidAsConstant(Expression expr, object value, Type type)
        {
            Debug.Assert(expr.Type == typeof(void));

            // block{ expr; return constant; }

            var constant = Expression.Constant(value, type);

            return Expression.Block(expr, constant);
        }

        #endregion

        #region BindDefault

        public static Expression BindDefault(Type t)
        {
            if (t == typeof(PhpValue)) return Expression.Field(null, typeof(PhpValue), "Void");
            if (t == typeof(PhpNumber)) return Expression.Field(null, typeof(PhpNumber), "Default");

            return Expression.Default(t);
        }

        #endregion

        #region BindCost

        /// <summary>
        /// Creates expression that calculates cost of conversion from <paramref name="arg"/> to type <paramref name="target"/>.
        /// In some cases, returned expression is a constant and can be used in compile time.
        /// </summary>
        /// <param name="arg">Expression to be converted.</param>
        /// <param name="target">Target type.</param>
        /// <returns>Expression calculating the cost of conversion.</returns>
        public static Expression BindCost(Expression arg, Type target)
        {
            if (arg == null || target == null)
            {
                throw new ArgumentNullException();
            }

            var t = arg.Type;
            if (t == target)
            {
                return Expression.Constant(ConversionCost.Pass);
            }

            if (t == typeof(PhpValue)) return BindCostFromValue(arg, target);
            if (t == typeof(double)) return Expression.Constant(BindCostFromDouble(arg, target));
            if (t == typeof(long) || t == typeof(int)) return Expression.Constant(BindCostFromLong(arg, target));
            if (t == typeof(PhpNumber)) return BindCostFromNumber(arg, target);
            if (t == typeof(string)) return Expression.Constant(BindCostFromString(arg, target));
            if (t == typeof(PhpString)) return Expression.Constant(BindCostFromPhpString(arg, target));

            // other types
            if (t.GetTypeInfo().IsAssignableFrom(target.GetTypeInfo())) return Expression.Constant(ConversionCost.Pass);

            //
            throw new NotImplementedException($"costof({t} -> {target})");
        }

        static Expression BindCostFromValue(Expression arg, Type target)
        {
            // constant cases
            if (target == typeof(PhpValue)) return Expression.Constant(ConversionCost.Pass);

            //
            var target_type = target.GetTypeInfo();
            if (!target_type.IsValueType)
            {
                // TODO
            }

            //
            if (target_type.IsEnum)
            {
                return Expression.Call(typeof(CostOf).GetMethod("ToInt64", arg.Type), arg);
            }

            // fallback
            return Expression.Call(typeof(CostOf).GetMethod("To" + target.Name, arg.Type), arg);
        }

        static ConversionCost BindCostFromDouble(Expression arg, Type target)
        {
            if (target == typeof(double)) return (ConversionCost.Pass);
            if (target == typeof(PhpNumber)) return (ConversionCost.PassCostly);
            if (target == typeof(long)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(bool)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(PhpValue)) return (ConversionCost.PassCostly);
            if (target == typeof(string) || target == typeof(PhpString)) return (ConversionCost.ImplicitCast);
            if (target == typeof(PhpArray)) return (ConversionCost.Warning);

            throw new NotImplementedException($"costof(double -> {target})");
        }

        static ConversionCost BindCostFromLong(Expression arg, Type target)
        {
            if (target == typeof(long)) return (ConversionCost.Pass);
            if (target == typeof(PhpNumber)) return (ConversionCost.PassCostly);
            if (target == typeof(double)) return (ConversionCost.ImplicitCast);
            if (target == typeof(bool)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(PhpValue)) return (ConversionCost.PassCostly);
            if (target == typeof(string) || target == typeof(PhpString)) return (ConversionCost.ImplicitCast);
            if (target == typeof(PhpArray)) return (ConversionCost.Warning);
            if (target == typeof(object)) return ConversionCost.PassCostly;    // TODO: Error when passing to a PHP function

            throw new NotImplementedException($"costof(long -> {target})");
        }

        static Expression BindCostFromNumber(Expression arg, Type target)
        {
            if (target == typeof(double) || target == typeof(long) || target == typeof(int))
            {
                return Expression.Call(typeof(CostOf).GetMethod("To" + target.Name, arg.Type), arg);
            }

            if (target == typeof(PhpNumber)) return Expression.Constant(ConversionCost.Pass);
            if (target == typeof(string)) return Expression.Constant(ConversionCost.ImplicitCast);
            if (target == typeof(bool)) return Expression.Constant(ConversionCost.LoosingPrecision);
            if (target == typeof(PhpValue)) return Expression.Constant(ConversionCost.PassCostly);
            
            return Expression.Constant(ConversionCost.Warning);
        }

        static ConversionCost BindCostFromString(Expression arg, Type target)
        {
            if (target == typeof(bool)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(long)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(double)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(PhpNumber)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(string)) return (ConversionCost.Pass);
            if (target == typeof(PhpString)) return (ConversionCost.PassCostly);
            if (target == typeof(PhpValue)) return (ConversionCost.PassCostly);
            if (target == typeof(object)) return ConversionCost.PassCostly;    // TODO: Error when passing to a PHP function

            var tinfo = target.GetTypeInfo();
            if (tinfo.IsAssignableFrom(typeof(IPhpCallable).GetTypeInfo())) throw new NotImplementedException("IPhpCallable");

            return ConversionCost.Error;
        }

        static ConversionCost BindCostFromPhpString(Expression arg, Type target)
        {
            if (target == typeof(bool)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(long)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(double)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(PhpNumber)) return (ConversionCost.LoosingPrecision);
            if (target == typeof(string)) return (ConversionCost.PassCostly);
            if (target == typeof(PhpString)) return (ConversionCost.Pass);
            if (target == typeof(PhpValue)) return (ConversionCost.PassCostly);
            if (target == typeof(object)) return ConversionCost.PassCostly;    // TODO: Error when passing to a PHP function

            var tinfo = target.GetTypeInfo();
            if (tinfo.IsAssignableFrom(typeof(IPhpCallable).GetTypeInfo())) throw new NotImplementedException("IPhpCallable");

            return ConversionCost.Error;
        }

        #endregion
    }

    /// <summary>
    /// Runtime routines that calculates cost of conversion.
    /// </summary>
    public static class CostOf
    {
        /// <summary>
        /// Gets minimal value of given operands.
        /// </summary>
        public static ConversionCost Min(ConversionCost a, ConversionCost b) => (a < b) ? a : b;

        /// <summary>
        /// Gets maximal value of given operands.
        /// </summary>
        public static ConversionCost Max(ConversionCost a, ConversionCost b) => (a > b) ? a : b;

        public static ConversionCost Or(ConversionCost a, ConversionCost b) => a | b;

        #region CostOf

        public static ConversionCost ToInt32(PhpNumber value) => ToInt64(value);

        public static ConversionCost ToInt64(PhpNumber value) => value.IsLong ? ConversionCost.Pass : ConversionCost.LoosingPrecision;

        public static ConversionCost ToDouble(PhpNumber value) => value.IsLong ? ConversionCost.ImplicitCast : ConversionCost.Pass;

        public static ConversionCost ToInt32(PhpValue value) => ToInt64(value);

        public static ConversionCost ToInt64(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                    return ConversionCost.Pass;

                case PhpTypeCode.Boolean:
                    return ConversionCost.ImplicitCast;

                case PhpTypeCode.Double:
                case PhpTypeCode.WritableString:
                case PhpTypeCode.String:
                    return ConversionCost.LoosingPrecision;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Warning;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToString(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                case PhpTypeCode.Boolean:
                case PhpTypeCode.Double:
                case PhpTypeCode.Object:
                    return ConversionCost.ImplicitCast;

                case PhpTypeCode.WritableString:
                    return value.WritableString.ContainsBinaryData ? ConversionCost.ImplicitCast : ConversionCost.PassCostly;

                case PhpTypeCode.String:
                    return ConversionCost.Pass;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Warning;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToPhpString(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                case PhpTypeCode.Boolean:
                case PhpTypeCode.Double:
                case PhpTypeCode.Object:
                    return ConversionCost.ImplicitCast;

                case PhpTypeCode.WritableString:
                    return ConversionCost.PassCostly;

                case PhpTypeCode.String:
                    return ConversionCost.Pass;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Warning;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToDouble(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                case PhpTypeCode.Boolean:
                    return ConversionCost.ImplicitCast;

                case PhpTypeCode.Double:
                    return ConversionCost.Pass;

                case PhpTypeCode.WritableString:
                case PhpTypeCode.String:
                    return ConversionCost.LoosingPrecision;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Warning;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToPhpNumber(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                case PhpTypeCode.Double:
                    return ConversionCost.Pass;

                case PhpTypeCode.Boolean:
                    return ConversionCost.ImplicitCast;

                case PhpTypeCode.WritableString:
                case PhpTypeCode.String:
                    return ConversionCost.LoosingPrecision;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Warning;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        public static ConversionCost ToPhpArray(PhpValue value)
        {
            switch (value.TypeCode)
            {
                case PhpTypeCode.Int32:
                case PhpTypeCode.Long:
                case PhpTypeCode.Double:
                case PhpTypeCode.Boolean:
                case PhpTypeCode.WritableString:
                case PhpTypeCode.String:
                    return ConversionCost.Warning;

                case PhpTypeCode.PhpArray:
                    return ConversionCost.Pass;

                default:
                    return ConversionCost.NoConversion;
            }
        }

        #endregion
    }
}
