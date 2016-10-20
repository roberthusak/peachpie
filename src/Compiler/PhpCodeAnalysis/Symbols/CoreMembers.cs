﻿using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pchp.CodeAnalysis.Symbols
{
    #region CoreMethod, CoreConstructor, CoreOperator

    /// <summary>
    /// Descriptor of a well-known method.
    /// </summary>
    [DebuggerDisplay("CoreMethod {DebuggerDisplay,nq}")]
    class CoreMethod
    {
        #region Fields

        /// <summary>
        /// Lazily associated symbol.
        /// </summary>
        MethodSymbol _lazySymbol;

        /// <summary>
        /// Parametyer types.
        /// </summary>
        readonly CoreType[] _ptypes;

        /// <summary>
        /// Declaring class. Cannot be <c>null</c>.
        /// </summary>
        public readonly CoreType DeclaringClass;

        /// <summary>
        /// The method name.
        /// </summary>
        public readonly string MethodName;

        #endregion

        public CoreMethod(CoreType declaringClass, string methodName, params CoreType[] ptypes)
        {
            Contract.ThrowIfNull(declaringClass);
            Contract.ThrowIfNull(methodName);

            this.DeclaringClass = declaringClass;
            this.MethodName = methodName;

            _ptypes = ptypes;
        }

        /// <summary>
        /// Gets associated symbol.
        /// </summary>
        public MethodSymbol Symbol
        {
            get
            {
                var symbol = _lazySymbol;
                if (symbol == null)
                {
                    symbol = ResolveSymbol();
                    Contract.ThrowIfNull(symbol);

                    Interlocked.CompareExchange(ref _lazySymbol, symbol, null);
                }
                return symbol;
            }
        }

        string DebuggerDisplay => DeclaringClass.FullName + "." + MethodName;

        /// <summary>
        /// Implicit cast to method symbol.
        /// </summary>
        public static implicit operator MethodSymbol(CoreMethod m) => m.Symbol;

        #region ResolveSymbol

        /// <summary>
        /// Resolves <see cref="MethodSymbol"/> of this descriptor.
        /// </summary>
        protected virtual MethodSymbol ResolveSymbol()
        {
            var type = this.DeclaringClass.Symbol;
            if (type == null)
                throw new InvalidOperationException();

            var methods = type.GetMembers(MethodName);
            return methods.OfType<MethodSymbol>().First(MatchesSignature);
        }

        protected bool MatchesSignature(MethodSymbol m)
        {
            var ps = m.Parameters;
            if (ps.Length != _ptypes.Length)
                return false;

            for (int i = 0; i < ps.Length; i++)
                if (_ptypes[i] != ps[i].Type)
                    return false;

            return true;
        }

        #endregion
    }

    class CoreField
    {
        #region Fields

        /// <summary>
        /// Lazily associated symbol.
        /// </summary>
        FieldSymbol _lazySymbol;

        /// <summary>
        /// Declaring class. Cannot be <c>null</c>.
        /// </summary>
        public readonly CoreType DeclaringClass;

        /// <summary>
        /// The field name.
        /// </summary>
        public readonly string FieldName;

        #endregion

        public CoreField(CoreType declaringClass, string fldName)
        {
            Contract.ThrowIfNull(declaringClass);
            Contract.ThrowIfNull(fldName);

            this.DeclaringClass = declaringClass;
            this.FieldName = fldName;
        }

        /// <summary>
        /// Gets associated symbol.
        /// </summary>
        public FieldSymbol Symbol
        {
            get
            {
                var symbol = _lazySymbol;
                if (symbol == null)
                {
                    symbol = ResolveSymbol();
                    Contract.ThrowIfNull(symbol);

                    Interlocked.CompareExchange(ref _lazySymbol, symbol, null);
                }
                return symbol;
            }
        }

        string DebuggerDisplay => DeclaringClass.FullName + "." + FieldName;

        /// <summary>
        /// Implicit cast to field symbol.
        /// </summary>
        public static implicit operator FieldSymbol(CoreField m) => m.Symbol;

        #region ResolveSymbol

        /// <summary>
        /// Resolves <see cref="FieldSymbol"/> of this descriptor.
        /// </summary>
        protected virtual FieldSymbol ResolveSymbol()
        {
            var type = this.DeclaringClass.Symbol;
            if (type == null)
                throw new InvalidOperationException();

            var fields = type.GetMembers(FieldName);
            return fields.OfType<FieldSymbol>().First();
        }

        #endregion
    }

    /// <summary>
    /// Descriptor of a well-known constructor.
    /// </summary>
    class CoreConstructor : CoreMethod
    {
        public CoreConstructor(CoreType declaringClass, params CoreType[] ptypes)
            : base(declaringClass, ".ctor", ptypes)
        {

        }

        protected override MethodSymbol ResolveSymbol()
        {
            var type = this.DeclaringClass.Symbol;
            if (type == null)
                throw new InvalidOperationException();

            var methods = type.InstanceConstructors;
            return methods.First(MatchesSignature);
        }
    }

    /// <summary>
    /// Descriptor of a well-known operator method.
    /// </summary>
    class CoreOperator : CoreMethod
    {
        /// <summary>
        /// Creates the descriptor.
        /// </summary>
        /// <param name="declaringClass">Containing class.</param>
        /// <param name="name">Operator name, without <c>op_</c> prefix.</param>
        /// <param name="ptypes">CLR parameters.</param>
        public CoreOperator(CoreType declaringClass, string name, params CoreType[] ptypes)
            : base(declaringClass, name, ptypes)
        {
            Debug.Assert(name.StartsWith("op_"));
        }

        protected override MethodSymbol ResolveSymbol()
        {
            var type = this.DeclaringClass.Symbol;
            if (type == null)
                throw new InvalidOperationException();

            var methods = type.GetMembers(this.MethodName);
            return methods.OfType<MethodSymbol>()
                .Where(m => m.HasSpecialName)
                .First(MatchesSignature);
        }
    }

    class CoreExplicitCast : CoreMethod
    {
        readonly CoreType _castTo;

        public CoreExplicitCast(CoreType declaringClass, CoreType castTo)
            : base(declaringClass, WellKnownMemberNames.ExplicitConversionName, declaringClass)
        {
            _castTo = castTo;
        }

        protected override MethodSymbol ResolveSymbol()
        {
            var type = this.DeclaringClass.Symbol;
            if (type == null)
                throw new InvalidOperationException();

            var methods = type.GetMembers(this.MethodName);
            return methods.OfType<MethodSymbol>()
                .Where(m => m.HasSpecialName && m.IsStatic && m.ParameterCount == 1 && m.Parameters[0].Type == type && m.ReturnType == _castTo)
                .First();
        }
    }

    #endregion

    /// <summary>
    /// Set of well-known methods declared in a core library.
    /// </summary>
    class CoreMethods
    {
        public readonly PhpValueHolder PhpValue;
        public readonly PhpAliasHolder PhpAlias;
        public readonly PhpArrayHolder PhpArray;
        public readonly IPhpArrayHolder IPhpArray;
        public readonly PhpNumberHolder PhpNumber;
        public readonly OperatorsHolder Operators;
        public readonly PhpStringHolder PhpString;
        public readonly ConstructorsHolder Ctors;
        public readonly ContextHolder Context;
        public readonly DynamicHolder Dynamic;
        public readonly ReflectionHolder Reflection;

        public CoreMethods(CoreTypes types)
        {
            Contract.ThrowIfNull(types);

            PhpValue = new PhpValueHolder(types);
            PhpAlias = new PhpAliasHolder(types);
            PhpArray = new PhpArrayHolder(types);
            IPhpArray = new IPhpArrayHolder(types);
            PhpNumber = new PhpNumberHolder(types);
            PhpString = new PhpStringHolder(types);
            Operators = new OperatorsHolder(types);
            Ctors = new ConstructorsHolder(types);
            Context = new ContextHolder(types);
            Dynamic = new DynamicHolder(types);
            Reflection = new ReflectionHolder(types);
        }

        public struct OperatorsHolder
        {
            public OperatorsHolder(CoreTypes ct)
            {
                SetValue_PhpValueRef_PhpValue = ct.Operators.Method("SetValue", ct.PhpValue, ct.PhpValue);
                EnsureObject_ObjectRef = ct.Operators.Method("EnsureObject", ct.Object);
                EnsureArray_PhpArrayRef = ct.Operators.Method("EnsureArray", ct.PhpArray);
                EnsureArray_IPhpArrayRef = ct.Operators.Method("EnsureArray", ct.IPhpArray);
                IsSet_PhpValue = ct.Operators.Method("IsSet", ct.PhpValue);
                IsEmpty_PhpValue = ct.Operators.Method("IsEmpty", ct.PhpValue);

                ToString_Bool = ct.Convert.Method("ToString", ct.Boolean);
                ToString_Int32 = ct.Convert.Method("ToString", ct.Int32);
                ToString_Long = ct.Convert.Method("ToString", ct.Long);
                ToString_Double_Context = ct.Convert.Method("ToString", ct.Double, ct.Context);
                Long_ToString = ct.Long.Method("ToString");
                ToBoolean_String = ct.Convert.Method("ToBoolean", ct.String);
                ToBoolean_PhpValue = new CoreExplicitCast(ct.PhpValue, ct.Boolean);
                ToBoolean_Object = ct.Convert.Method("ToBoolean", ct.Object);
                ToLong_PhpValue = new CoreExplicitCast(ct.PhpValue, ct.Long);
                ToDouble_PhpValue = new CoreExplicitCast(ct.PhpValue, ct.Double);

                AsObject_PhpValue = ct.Convert.Method("AsObject", ct.PhpValue);
                AsArray_PhpValue = ct.Convert.Method("AsArray", ct.PhpValue);
                ToClass_PhpValue = ct.Convert.Method("ToClass", ct.PhpValue);
                ToClass_IPhpArray = ct.Convert.Method("ToClass", ct.IPhpArray);
                AsCallable_PhpValue = ct.Convert.Method("AsCallable", ct.PhpValue);
                AsCallable_String = ct.Convert.Method("AsCallable", ct.String);
                ToIntStringKey_PhpValue = ct.Convert.Method("ToIntStringKey", ct.PhpValue);

                Echo_String = ct.Context.Method("Echo", ct.String);
                Echo_PhpString = ct.Context.Method("Echo", ct.PhpString);
                Echo_PhpNumber = ct.Context.Method("Echo", ct.PhpNumber);
                Echo_PhpValue = ct.Context.Method("Echo", ct.PhpValue);
                Echo_Object = ct.Context.Method("Echo", ct.Object);
                Echo_Double = ct.Context.Method("Echo", ct.Double);
                Echo_Long = ct.Context.Method("Echo", ct.Long);
                Echo_Int32 = ct.Context.Method("Echo", ct.Int32);

                GetForeachEnumerator_PhpValue_Bool_RuntimeTypeHandle = ct.Operators.Method("GetForeachEnumerator", ct.PhpValue, ct.Boolean, ct.RuntimeTypeHandle);

                Ceq_long_double = ct.Comparison.Method("Ceq", ct.Long, ct.Double);
                Ceq_long_bool = ct.Comparison.Method("Ceq", ct.Long, ct.Boolean);
                Ceq_long_string = ct.Comparison.Method("Ceq", ct.Long, ct.String);
                Ceq_double_string = ct.Comparison.Method("Ceq", ct.Double, ct.String);
                Ceq_string_long = ct.Comparison.Method("Ceq", ct.String, ct.Long);
                Ceq_string_double = ct.Comparison.Method("Ceq", ct.String, ct.Double);
                Ceq_string_bool = ct.Comparison.Method("Ceq", ct.String, ct.Boolean);
                Clt_long_double = ct.Comparison.Method("Clt", ct.Long, ct.Double);
                Cgt_long_double = ct.Comparison.Method("Cgt", ct.Long, ct.Double);
                Compare_bool_bool = ct.Comparison.Method("Compare", ct.Boolean, ct.Boolean);
                Compare_number_value = ct.Comparison.Method("Compare", ct.PhpNumber, ct.PhpValue);
                Compare_long_value = ct.Comparison.Method("Compare", ct.Long, ct.PhpValue);
                Compare_double_value = ct.Comparison.Method("Compare", ct.Double, ct.PhpValue);
                Compare_bool_value = ct.Comparison.Method("Compare", ct.Boolean, ct.PhpValue);
                Compare_value_value = ct.Comparison.Method("Compare", ct.PhpValue, ct.PhpValue);
                Compare_string_string = ct.Comparison.Method("Compare", ct.String, ct.String);
                Compare_string_long = ct.Comparison.Method("Compare", ct.String, ct.Long);
                Compare_string_double = ct.Comparison.Method("Compare", ct.String, ct.Double);
                Compare_string_value = ct.Comparison.Method("Compare", ct.String, ct.PhpValue);

                StrictCeq_bool_PhpValue = ct.StrictComparison.Method("Ceq", ct.Boolean, ct.PhpValue);
                StrictCeq_long_PhpValue = ct.StrictComparison.Method("Ceq", ct.Long, ct.PhpValue);
                StrictCeq_double_PhpValue = ct.StrictComparison.Method("Ceq", ct.Double, ct.PhpValue);
                StrictCeq_PhpValue_PhpValue = ct.StrictComparison.Method("Ceq", ct.PhpValue, ct.PhpValue);
                StrictCeq_PhpValue_bool = ct.StrictComparison.Method("Ceq", ct.PhpValue, ct.Boolean);

                Div_PhpValue_PhpValue = ct.PhpValue.Method(WellKnownMemberNames.DivisionOperatorName, ct.PhpValue, ct.PhpValue);
                BitwiseOr_PhpValue_PhpValue = ct.PhpValue.Method(WellKnownMemberNames.BitwiseOrOperatorName, ct.PhpValue, ct.PhpValue);
            }

            public readonly CoreMethod
                SetValue_PhpValueRef_PhpValue, EnsureObject_ObjectRef, EnsureArray_PhpArrayRef, EnsureArray_IPhpArrayRef,
                IsSet_PhpValue, IsEmpty_PhpValue,
                ToString_Bool, ToString_Long, ToString_Int32, ToString_Double_Context, Long_ToString,
                ToBoolean_String, ToBoolean_PhpValue, ToBoolean_Object,
                ToLong_PhpValue, ToDouble_PhpValue,
                AsObject_PhpValue, AsArray_PhpValue, ToClass_PhpValue, ToClass_IPhpArray, AsCallable_PhpValue, AsCallable_String,
                ToIntStringKey_PhpValue,
                Echo_Object, Echo_String, Echo_PhpString, Echo_PhpNumber, Echo_PhpValue, Echo_Double, Echo_Long, Echo_Int32,

                GetForeachEnumerator_PhpValue_Bool_RuntimeTypeHandle,

                Ceq_long_double, Ceq_long_bool, Ceq_long_string, Ceq_double_string, Ceq_string_long, Ceq_string_double, Ceq_string_bool,
                Clt_long_double, Cgt_long_double,
                Compare_bool_bool, Compare_number_value,
                Compare_long_value, Compare_value_value, Compare_double_value, Compare_bool_value, Compare_string_string, Compare_string_long, Compare_string_double, Compare_string_value,

                StrictCeq_bool_PhpValue, StrictCeq_long_PhpValue, StrictCeq_double_PhpValue, StrictCeq_PhpValue_PhpValue,
                StrictCeq_PhpValue_bool,

                Div_PhpValue_PhpValue,
                BitwiseOr_PhpValue_PhpValue;
        }

        public struct PhpValueHolder
        {
            public PhpValueHolder(CoreTypes ct)
            {
                ToBoolean = ct.PhpValue.Method("ToBoolean");
                ToLong = ct.PhpValue.Method("ToLong");
                ToDouble = ct.PhpValue.Method("ToDouble");
                ToString_Context = ct.PhpValue.Method("ToString", ct.Context);
                ToClass = ct.PhpValue.Method("ToClass");
                EnsureObject = ct.PhpValue.Method("EnsureObject");
                EnsureArray = ct.PhpValue.Method("EnsureArray");
                EnsureAlias = ct.PhpValue.Method("EnsureAlias");

                Eq_PhpValue_PhpValue = ct.PhpValue.Operator(WellKnownMemberNames.EqualityOperatorName, ct.PhpValue, ct.PhpValue);

                DeepCopy = ct.PhpValue.Method("DeepCopy");
                AsArray = ct.PhpValue.Method("AsArray");
                AsObject = ct.PhpValue.Method("AsObject");

                get_Long = ct.PhpValue.Method("get_Long");   // TODO: special name, property
                get_Double = ct.PhpValue.Method("get_Double");   // TODO: special name, property
                get_Boolean = ct.PhpValue.Method("get_Boolean");   // TODO: special name, property
                get_String = ct.PhpValue.Method("get_String");   // TODO: special name, property
                get_Object = ct.PhpValue.Method("get_Object");   // TODO: special name, property
                get_Array = ct.PhpValue.Method("get_Array");   // TODO: special name, property

                Create_Boolean = ct.PhpValue.Method("Create", ct.Boolean);
                Create_Long = ct.PhpValue.Method("Create", ct.Long);
                Create_Double = ct.PhpValue.Method("Create", ct.Double);
                Create_String = ct.PhpValue.Method("Create", ct.String);
                Create_PhpString = ct.PhpValue.Method("Create", ct.PhpString);
                Create_PhpNumber = ct.PhpValue.Method("Create", ct.PhpNumber);
                Create_PhpArray = ct.PhpValue.Method("Create", ct.PhpArray);
                Create_IPhpArray = ct.PhpValue.Method("Create", ct.IPhpArray);
                Create_PhpAlias = ct.PhpValue.Method("Create", ct.PhpAlias);
                Create_IntStringKey = ct.PhpValue.Method("Create", ct.IntStringKey);

                FromClr_Object = ct.PhpValue.Method("FromClr", ct.Object);
                FromClass_Object = ct.PhpValue.Method("FromClass", ct.Object);

                Void = ct.PhpValue.Field("Void");
                Null = ct.PhpValue.Field("Null");
            }

            public readonly CoreMethod
                ToLong, ToDouble, ToBoolean, ToString_Context, ToClass, EnsureObject, EnsureArray, EnsureAlias,
                AsArray, AsObject,
                DeepCopy,
                Eq_PhpValue_PhpValue,
                get_Long, get_Double, get_Boolean, get_String, get_Object, get_Array,
                Create_Boolean, Create_Long, Create_Double, Create_String, Create_PhpString, Create_PhpNumber, Create_PhpAlias, Create_PhpArray, Create_IPhpArray, Create_IntStringKey,
                FromClr_Object, FromClass_Object;

            public readonly CoreField
                Void, Null;
        }

        public struct PhpAliasHolder
        {
            public PhpAliasHolder(CoreTypes ct)
            {
                _value = null;

                EnsureObject = ct.PhpAlias.Method("EnsureObject");
                EnsureArray = ct.PhpAlias.Method("EnsureArray");
            }

            public readonly CoreMethod
                EnsureObject, EnsureArray;

            /// <summary>
            /// Lazily gets <c>PhpAlias.Value</c> field.
            /// </summary>
            public FieldSymbol Value
            {
                get
                {
                    if (_value == null)
                    {
                        Debug.Assert(EnsureObject.DeclaringClass.Symbol != null);
                        _value = (EnsureObject.DeclaringClass.Symbol)
                            .GetMembers("Value").OfType<FieldSymbol>().First();
                        Debug.Assert(_value != null);
                    }
                    return _value;
                }
            }
            FieldSymbol _value;
        }

        public struct PhpNumberHolder
        {
            public PhpNumberHolder(CoreTypes ct)
            {
                ToBoolean = ct.PhpNumber.Method("ToBoolean");
                ToLong = ct.PhpNumber.Method("ToLong");
                ToDouble = ct.PhpNumber.Method("ToDouble");
                ToString_Context = ct.PhpNumber.Method("ToString", ct.Context);
                ToClass = ct.PhpNumber.Method("ToClass");

                CompareTo_number = ct.PhpNumber.Method("CompareTo", ct.PhpNumber);
                CompareTo_long = ct.PhpNumber.Method("CompareTo", ct.Long);
                CompareTo_double = ct.PhpNumber.Method("CompareTo", ct.Double);

                Create_Long = ct.PhpNumber.Method("Create", ct.Long);
                Create_Double = ct.PhpNumber.Method("Create", ct.Double);
                Default = ct.PhpNumber.Field("Default");

                get_Long = ct.PhpNumber.Method("get_Long");   // TODO: special name, property
                get_Double = ct.PhpNumber.Method("get_Double");   // TODO: special name, property

                Eq_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.EqualityOperatorName, ct.PhpNumber, ct.PhpNumber);
                Ineq_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.InequalityOperatorName, ct.PhpNumber, ct.PhpNumber);
                Eq_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.EqualityOperatorName, ct.PhpNumber, ct.Long);
                Ineq_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.InequalityOperatorName, ct.PhpNumber, ct.Long);
                Eq_number_double = ct.PhpNumber.Operator(WellKnownMemberNames.EqualityOperatorName, ct.PhpNumber, ct.Double);
                Ineq_number_double = ct.PhpNumber.Operator(WellKnownMemberNames.InequalityOperatorName, ct.PhpNumber, ct.Double);

                Add_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.AdditionOperatorName, ct.PhpNumber, ct.PhpNumber);
                Add_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.AdditionOperatorName, ct.PhpNumber, ct.Long);
                Add_long_number = ct.PhpNumber.Operator(WellKnownMemberNames.AdditionOperatorName, ct.Long, ct.PhpNumber);
                Add_double_number = ct.PhpNumber.Method("Add", ct.Double, ct.PhpNumber);
                Add_number_double = ct.PhpNumber.Method("Add", ct.PhpNumber, ct.Double);
                Add_long_long = ct.PhpNumber.Method("Add", ct.Long, ct.Long);
                Add_long_double = ct.PhpNumber.Method("Add", ct.Long, ct.Double);
                Add_value_long = ct.PhpNumber.Method("Add", ct.PhpValue, ct.Long);
                Add_value_double = ct.PhpNumber.Method("Add", ct.PhpValue, ct.Double);
                Add_value_number = ct.PhpNumber.Method("Add", ct.PhpValue, ct.PhpNumber);
                Add_long_value = ct.PhpNumber.Method("Add", ct.Long, ct.PhpValue);
                Add_double_value = ct.PhpNumber.Method("Add", ct.Double, ct.PhpValue);
                Add_number_value = ct.PhpNumber.Method("Add", ct.PhpNumber, ct.PhpValue);
                Add_value_value = ct.PhpNumber.Method("Add", ct.PhpValue, ct.PhpValue);

                Subtract_long_long = ct.PhpNumber.Method("Sub", ct.Long, ct.Long);
                Subtract_number_double = ct.PhpNumber.Method("Sub", ct.PhpNumber, ct.Double);
                Subtract_long_double = ct.PhpNumber.Method("Sub", ct.Long, ct.Double);
                Subtract_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.SubtractionOperatorName, ct.PhpNumber, ct.PhpNumber);
                Subtract_long_number = ct.PhpNumber.Operator(WellKnownMemberNames.SubtractionOperatorName, ct.Long, ct.PhpNumber);
                Subtract_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.SubtractionOperatorName, ct.PhpNumber, ct.Long);
                Subtract_value_value = ct.PhpNumber.Method("Sub", ct.PhpValue, ct.PhpValue);
                Subtract_value_long = ct.PhpNumber.Method("Sub", ct.PhpValue, ct.Long);
                Subtract_value_double = ct.PhpNumber.Method("Sub", ct.PhpValue, ct.Double);
                Subtract_value_number = ct.PhpNumber.Method("Sub", ct.PhpValue, ct.PhpNumber);

                Negation = ct.PhpNumber.Operator(WellKnownMemberNames.UnaryNegationOperatorName, ct.PhpNumber);
                Negation_long = ct.PhpNumber.Method("Minus", ct.Long);

                Division_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.DivisionOperatorName, ct.PhpNumber, ct.PhpNumber);
                Division_long_number = ct.PhpNumber.Operator(WellKnownMemberNames.DivisionOperatorName, ct.Long, ct.PhpNumber);

                Mul_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.MultiplyOperatorName, ct.PhpNumber, ct.PhpNumber);
                Mul_number_double = ct.PhpNumber.Operator(WellKnownMemberNames.MultiplyOperatorName, ct.PhpNumber, ct.Double);
                Mul_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.MultiplyOperatorName, ct.PhpNumber, ct.Long);
                Mul_long_number = ct.PhpNumber.Operator(WellKnownMemberNames.MultiplyOperatorName, ct.Long, ct.PhpNumber);
                Mul_number_value = ct.PhpNumber.Operator(WellKnownMemberNames.MultiplyOperatorName, ct.PhpNumber, ct.PhpValue);
                Mul_value_number = ct.PhpNumber.Operator(WellKnownMemberNames.MultiplyOperatorName, ct.PhpNumber, ct.PhpValue);
                Mul_long_long = ct.PhpNumber.Method("Multiply", ct.Long, ct.Long);
                Mul_long_double = ct.PhpNumber.Method("Multiply", ct.Long, ct.Double);
                Mul_double_value = ct.PhpNumber.Method("Multiply", ct.Double, ct.PhpValue);
                Mul_long_value = ct.PhpNumber.Method("Multiply", ct.Long, ct.PhpValue);
                Mul_value_value = ct.PhpNumber.Method("Multiply", ct.PhpValue, ct.PhpValue);
                Mul_value_long = ct.PhpNumber.Method("Multiply", ct.PhpValue, ct.Long);
                Mul_value_double = ct.PhpNumber.Method("Multiply", ct.PhpValue, ct.Double);

                Pow_value_value = ct.PhpNumber.Method("Pow", ct.PhpValue, ct.PhpValue);
                Pow_number_number = ct.PhpNumber.Method("Pow", ct.PhpNumber, ct.PhpNumber);
                Pow_number_double = ct.PhpNumber.Method("Pow", ct.PhpNumber, ct.Double);
                Pow_number_value = ct.PhpNumber.Method("Pow", ct.PhpNumber, ct.PhpValue);
                Pow_double_double = ct.PhpNumber.Method("Pow", ct.Double, ct.Double);
                Pow_double_value = ct.PhpNumber.Method("Pow", ct.Double, ct.PhpValue);
                Pow_long_long = ct.PhpNumber.Method("Pow", ct.Long, ct.Long);
                Pow_long_double = ct.PhpNumber.Method("Pow", ct.Long, ct.Double);
                Pow_long_number = ct.PhpNumber.Method("Pow", ct.Long, ct.PhpNumber);
                Pow_long_value = ct.PhpNumber.Method("Pow", ct.Long, ct.PhpValue);

                gt_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.GreaterThanOperatorName, ct.PhpNumber, ct.PhpNumber);
                gt_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.GreaterThanOperatorName, ct.PhpNumber, ct.Long);
                gt_number_double = ct.PhpNumber.Operator(WellKnownMemberNames.GreaterThanOperatorName, ct.PhpNumber, ct.Double);
                lt_number_number = ct.PhpNumber.Operator(WellKnownMemberNames.LessThanOperatorName, ct.PhpNumber, ct.PhpNumber);
                lt_number_long = ct.PhpNumber.Operator(WellKnownMemberNames.LessThanOperatorName, ct.PhpNumber, ct.Long);
                lt_number_double = ct.PhpNumber.Operator(WellKnownMemberNames.LessThanOperatorName, ct.PhpNumber, ct.Double);
            }

            public readonly CoreMethod
                ToLong, ToDouble, ToBoolean, ToString_Context, ToClass,
                CompareTo_number, CompareTo_long, CompareTo_double,
                Add_long_long, Add_long_double, Add_number_double, Add_double_number, Add_value_long, Add_value_double, Add_value_number, Add_long_value, Add_double_value, Add_number_value, Add_value_value,
                Subtract_long_long, Subtract_number_double, Subtract_long_double, Subtract_value_value, Subtract_value_long, Subtract_value_double, Subtract_value_number,
                Negation_long,
                get_Long, get_Double,
                Mul_long_long, Mul_long_double, Mul_long_value, Mul_double_value, Mul_value_value, Mul_value_long, Mul_value_double,
                Pow_long_long, Pow_long_double, Pow_long_number, Pow_long_value, Pow_double_double, Pow_double_value, Pow_number_double, Pow_number_number, Pow_number_value, Pow_value_value,
                Create_Long, Create_Double;

            public readonly CoreOperator
                Eq_number_number, Ineq_number_number,
                Eq_number_long, Ineq_number_long,
                Eq_number_double, Ineq_number_double,
                Add_number_number, Add_number_long, Add_long_number,
                Subtract_number_number, Subtract_long_number, Subtract_number_long,
                Division_number_number, Division_long_number,
                Mul_number_number, Mul_number_double, Mul_number_long, Mul_long_number, Mul_number_value, Mul_value_number,
                gt_number_number, gt_number_long, gt_number_double,
                lt_number_number, lt_number_long, lt_number_double,
                Negation;

            public readonly CoreField
                Default;
        }

        public struct PhpStringHolder
        {
            public PhpStringHolder(CoreTypes ct)
            {
                ToBoolean = ct.PhpString.Method("ToBoolean");
                ToLong = ct.PhpString.Method("ToLong");
                ToDouble = ct.PhpString.Method("ToDouble");
                ToString_Context = ct.PhpString.Method("ToString", ct.Context);

                Append_String = ct.PhpString.Method("Append", ct.String);
                Append_PhpString = ct.PhpString.Method("Append", ct.PhpString);
                Append_PhpValue = ct.PhpString.Method("Append", ct.PhpValue);
            }

            public readonly CoreMethod
                ToLong, ToDouble, ToBoolean, ToString_Context,
                Append_String, Append_PhpString, Append_PhpValue;
        }

        public struct IPhpArrayHolder
        {
            public IPhpArrayHolder(CoreTypes ct)
            {
                var arr = ct.IPhpArray;

                RemoveKey_IntStringKey = arr.Method("RemoveKey", ct.IntStringKey);
                GetItemValue_IntStringKey = arr.Method("GetItemValue", ct.IntStringKey);
                SetItemValue_IntStringKey_PhpValue = arr.Method("SetItemValue", ct.IntStringKey, ct.PhpValue);
                SetItemAlias_IntStringKey_PhpAlias = arr.Method("SetItemAlias", ct.IntStringKey, ct.PhpAlias);
                AddValue_PhpValue = arr.Method("AddValue", ct.PhpValue);
                EnsureItemObject_IntStringKey = arr.Method("EnsureItemObject", ct.IntStringKey);
                EnsureItemArray_IntStringKey = arr.Method("EnsureItemArray", ct.IntStringKey);
                EnsureItemAlias_IntStringKey = arr.Method("EnsureItemAlias", ct.IntStringKey);

                get_Count = arr.Method("get_Count");
            }

            public readonly CoreMethod
                RemoveKey_IntStringKey,
                GetItemValue_IntStringKey,
                SetItemValue_IntStringKey_PhpValue, SetItemAlias_IntStringKey_PhpAlias, AddValue_PhpValue,
                EnsureItemObject_IntStringKey, EnsureItemArray_IntStringKey, EnsureItemAlias_IntStringKey,
                get_Count;
        }

        public struct PhpArrayHolder
        {
            public PhpArrayHolder(CoreTypes ct)
            {
                var t = ct.PhpArray;

                //
                ToString_Context = t.Method("ToString", ct.Context);
                ToClass = t.Method("ToClass");
                ToBoolean = t.Method("ToBoolean");

                RemoveKey_IntStringKey = t.Method("RemoveKey", ct.IntStringKey);

                GetItemValue_IntStringKey = t.Method("GetItemValue", ct.IntStringKey);

                DeepCopy = t.Method("DeepCopy");
                GetForeachEnumerator_Boolean = t.Method("GetForeachEnumerator", ct.Boolean);

                SetItemValue_IntStringKey_PhpValue = t.Method("SetItemValue", ct.IntStringKey, ct.PhpValue);
                SetItemAlias_IntStringKey_PhpAlias = t.Method("SetItemAlias", ct.IntStringKey, ct.PhpAlias);
                AddValue_PhpValue = t.Method("AddValue", ct.PhpValue);

                EnsureItemObject_IntStringKey = t.Method("EnsureItemObject", ct.IntStringKey);
                EnsureItemArray_IntStringKey = t.Method("EnsureItemArray", ct.IntStringKey);
                EnsureItemAlias_IntStringKey = t.Method("EnsureItemAlias", ct.IntStringKey);
            }

            public readonly CoreMethod
                ToClass, ToString_Context, ToBoolean,
                RemoveKey_IntStringKey,
                GetItemValue_IntStringKey,
                SetItemValue_IntStringKey_PhpValue, SetItemAlias_IntStringKey_PhpAlias, AddValue_PhpValue,
                EnsureItemObject_IntStringKey, EnsureItemArray_IntStringKey, EnsureItemAlias_IntStringKey,
                DeepCopy, GetForeachEnumerator_Boolean;
        }

        public struct ConstructorsHolder
        {
            public ConstructorsHolder(CoreTypes ct)
            {
                PhpAlias_PhpValue_int = ct.PhpAlias.Ctor(ct.PhpValue, ct.Int32);
                PhpString = ct.PhpString.Ctor();
                PhpString_string_string = ct.PhpString.Ctor(ct.String, ct.String);
                PhpArray = ct.PhpArray.Ctor();
                PhpArray_int = ct.PhpArray.Ctor(ct.Int32);
                IntStringKey_int = ct.IntStringKey.Ctor(ct.Int32);
                IntStringKey_string = ct.IntStringKey.Ctor(ct.String);
                ScriptAttribute_string = ct.ScriptAttribute.Ctor(ct.String);

                ScriptDiedException = ct.ScriptDiedException.Ctor();
                ScriptDiedException_Long = ct.ScriptDiedException.Ctor(ct.Long);
                ScriptDiedException_PhpValue = ct.ScriptDiedException.Ctor(ct.PhpValue);
            }

            public readonly CoreConstructor
                PhpAlias_PhpValue_int,
                PhpArray, PhpArray_int,
                PhpString, PhpString_string_string,
                IntStringKey_int, IntStringKey_string,
                ScriptAttribute_string,
                ScriptDiedException, ScriptDiedException_Long, ScriptDiedException_PhpValue;
        }

        public struct ContextHolder
        {
            public ContextHolder(CoreTypes ct)
            {
                AddScriptReference_TScript = ct.Context.Method("AddScriptReference");
                Dispose = ct.Context.Method("Dispose");

                DeclareFunction_RoutineInfo = ct.Context.Method("DeclareFunction", ct.RoutineInfo);
                DeclareType_T = ct.Context.Method("DeclareType");

                DisableErrorReporting = ct.Context.Method("DisableErrorReporting");
                EnableErrorReporting = ct.Context.Method("EnableErrorReporting");

                CheckIncludeOnce_TScript = ct.Context.Method("CheckIncludeOnce");
                OnInclude_TScript = ct.Context.Method("OnInclude");
                Include_string_string_PhpArray_object_bool_bool = ct.Context.Method("Include", ct.String, ct.String, ct.PhpArray, ct.Object, ct.Boolean, ct.Boolean);

                ScriptPath_TScript = ct.Context.Method("ScriptPath");

                GetConstant_string_int32 = ct.Context.Method("GetConstant", ct.String, ct.Int32);

                GetStatic_T = ct.Context.Method("GetStatic");
                GetDeclaredType_string = ct.Context.Method("GetDeclaredType", ct.String);

                get_Globals = ct.Context.Method("get_Globals");   // TODO: special name, property
                get_Server = ct.Context.Method("get_Server");   // TODO: special name, property
            }

            public readonly CoreMethod
                AddScriptReference_TScript,
                DeclareFunction_RoutineInfo, DeclareType_T,
                DisableErrorReporting, EnableErrorReporting,
                CheckIncludeOnce_TScript, OnInclude_TScript, Include_string_string_PhpArray_object_bool_bool,
                ScriptPath_TScript,
                GetConstant_string_int32,
                GetStatic_T,
                GetDeclaredType_string,
                Dispose;

            public readonly CoreMethod
                get_Globals, get_Server;
        }

        public struct DynamicHolder
        {
            public DynamicHolder(CoreTypes ct)
            {
                CallBinderFactory_Function = ct.CallBinderFactory.Method("Function", ct.String, ct.String, ct.RuntimeTypeHandle, ct.Int32);
                CallBinderFactory_InstanceFunction = ct.CallBinderFactory.Method("InstanceFunction", ct.String, ct.RuntimeTypeHandle, ct.RuntimeTypeHandle, ct.Int32);
                CallBinderFactory_StaticFunction = ct.CallBinderFactory.Method("StaticFunction", ct.RuntimeTypeHandle, ct.String, ct.RuntimeTypeHandle, ct.RuntimeTypeHandle, ct.Int32);
                GetFieldBinder_ctor = ct.GetFieldBinder.Ctor(ct.String, ct.RuntimeTypeHandle, ct.RuntimeTypeHandle, ct.AccessFlags);
                SetFieldBinder_ctor = ct.SetFieldBinder.Ctor(ct.String, ct.RuntimeTypeHandle, ct.AccessFlags);

                GetPhpTypeInfo_T = ct.PhpTypeInfoExtension.Method("GetPhpTypeInfo");
            }

            public readonly CoreConstructor
                GetFieldBinder_ctor, SetFieldBinder_ctor;

            public readonly CoreMethod
                CallBinderFactory_Function, CallBinderFactory_InstanceFunction, CallBinderFactory_StaticFunction,
                GetPhpTypeInfo_T;
        }

        public struct ReflectionHolder
        {
            public ReflectionHolder(CoreTypes ct)
            {
                CreateUserRoutine_string_RuntimeMethodHandle = ct.RoutineInfo.Method("CreateUserRoutine", ct.String, ct.RuntimeMethodHandle);
            }

            public CoreMethod CreateUserRoutine_string_RuntimeMethodHandle;
        }
    }
}
