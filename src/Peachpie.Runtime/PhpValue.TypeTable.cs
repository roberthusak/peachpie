﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Pchp.Core
{
    partial struct PhpValue
    {
        /// <summary>
        /// Methods table for <see cref="PhpValue"/> instance.
        /// </summary>
        abstract class TypeTable
        {
            #region Singletons

            public static readonly NullTable NullTable = new NullTable();
            public static readonly LongTable LongTable = new LongTable();
            public static readonly DoubleTable DoubleTable = new DoubleTable();
            public static readonly BoolTable BoolTable = new BoolTable();
            public static readonly StringTable StringTable = new StringTable();
            public static readonly TypeTable WritableStringTable = new WritableStringTable();
            public static readonly ClassTable ClassTable = new ClassTable();
            public static readonly ArrayTable ArrayTable = new ArrayTable();
            public static readonly AliasTable AliasTable = new AliasTable();

            #endregion

            public abstract PhpTypeCode Type { get; }
            public abstract bool IsNull { get; }
            public virtual bool IsEmpty(ref PhpValue me) => ToBoolean(ref me) == false;
            public abstract object ToClass(ref PhpValue me);
            public abstract string ToString(ref PhpValue me, Context ctx);
            public abstract string ToStringOrThrow(ref PhpValue me, Context ctx);
            public abstract long ToLong(ref PhpValue me);
            public abstract double ToDouble(ref PhpValue me);
            public abstract bool ToBoolean(ref PhpValue me);
            public abstract Convert.NumberInfo ToNumber(ref PhpValue me, out PhpNumber number);

            public abstract IntStringKey ToIntStringKey(ref PhpValue me);

            /// <summary>
            /// Gets enumerator object used within foreach statement.
            /// </summary>
            public abstract IPhpEnumerator GetForeachEnumerator(ref PhpValue me, bool aliasedValues, RuntimeTypeHandle caller);

            /// <summary>
            /// Compares two value operands.
            /// </summary>
            /// <param name="me">Reference to self, representing the left operand.</param>
            /// <param name="right">The right operand.</param>
            /// <returns>Comparison result.
            /// Zero for equality,
            /// negative value for <paramref name="me"/> &lt; <paramref name="right"/>,
            /// position value for <paramref name="me"/> &gt; <paramref name="right"/>.</returns>
            public abstract int Compare(ref PhpValue me, PhpValue right);

            /// <summary>
            /// Performs strict comparison.
            /// </summary>
            /// <param name="me">Reference to self, representing the left operand.</param>
            /// <param name="right">The right operand.</param>
            /// <returns>The value determining operands are strictly equal.</returns>
            public abstract bool StrictEquals(ref PhpValue me, PhpValue right);

            /// <summary>
            /// Ensures the value is a class object.
            /// In case it isn't, creates stdClass according to PHP semantics.
            /// In case current value is empty, replaces current value with newly created stdClass.
            /// </summary>
            /// <returns>Non-null object.</returns>
            public abstract object EnsureObject(ref PhpValue me);

            /// <summary>
            /// Ensures the value is a PHP array.
            /// In case it isn't, creates PhpArray according to PHP semantics.
            /// In case current value is empty, replaces current value with newly created array.
            /// </summary>
            /// <returns>Non-null object.</returns>
            public abstract IPhpArray EnsureArray(ref PhpValue me);

            /// <summary>
            /// Ensures the value as an alias.
            /// In case it isn't, the value is aliased.
            /// </summary>
            /// <returns>Non-null alias of the value.</returns>
            public virtual PhpAlias EnsureAlias(ref PhpValue me)
            {
                Debug.Assert(Type != PhpTypeCode.Alias, "To be overriden!");
                var alias = new PhpAlias(me, 1);
                me = Create(alias);
                return alias;
            }

            /// <summary>
            /// Gets object representing array access to the value.
            /// </summary>
            public abstract PhpArray AsArray(ref PhpValue me);

            /// <summary>
            /// Gets underlaying class instance or <c>null</c>.
            /// </summary>
            public virtual object AsObject(ref PhpValue me) => null;

            /// <summary>
            /// Gets callable wrapper for dynamic object invocation.
            /// </summary>
            /// <param name="me"></param>
            /// <returns>Instance of a callable object, cannot be <c>null</c>, can be invalid.</returns>
            public virtual IPhpCallable AsCallable(ref PhpValue me) => PhpCallback.CreateInvalid();

            /// <summary>
            /// Creates a deep copy of PHP variable.
            /// </summary>
            /// <returns>A deep copy of the value.</returns>
            public virtual PhpValue DeepCopy(ref PhpValue me) => me;

            /// <summary>
            /// Debug textual representation of the value.
            /// </summary>
            public abstract string DisplayString(ref PhpValue me);

            /// <summary>
            /// Calls corresponding <c>Accept</c> method on visitor.
            /// </summary>
            /// <param name="me">Reference to this.</param>
            /// <param name="visitor">Visitor to be called. Cannot be <c>null</c>.</param>
            public abstract void Accept(ref PhpValue me, PhpVariableVisitor visitor);
        }

        class NullTable : TypeTable
        {
            public override PhpTypeCode Type => PhpTypeCode.Object;
            public override bool IsNull => true;
            public override bool IsEmpty(ref PhpValue me) => true;
            public override object ToClass(ref PhpValue me) => new stdClass();
            public override string ToString(ref PhpValue me, Context ctx) => string.Empty;
            public override string ToStringOrThrow(ref PhpValue me, Context ctx) => string.Empty;
            public override long ToLong(ref PhpValue me) => 0;
            public override double ToDouble(ref PhpValue me) => 0.0;
            public override bool ToBoolean(ref PhpValue me) => false;
            public override Convert.NumberInfo ToNumber(ref PhpValue me, out PhpNumber number)
            {
                number = PhpNumber.Create(0L);
                return Convert.NumberInfo.LongInteger;
            }
            public override IntStringKey ToIntStringKey(ref PhpValue me) { throw new NotImplementedException(); }
            public override IPhpEnumerator GetForeachEnumerator(ref PhpValue me, bool aliasedValues, RuntimeTypeHandle caller) { throw new InvalidOperationException(); }
            public override int Compare(ref PhpValue me, PhpValue right) => Comparison.CompareNull(right);
            public override bool StrictEquals(ref PhpValue me, PhpValue right) => right.IsNull;
            public override object EnsureObject(ref PhpValue me)
            {
                var obj = ToClass(ref me);
                me = PhpValue.FromClass(obj);
                return obj;
            }
            public override IPhpArray EnsureArray(ref PhpValue me)
            {
                var arr = new PhpArray();
                me = PhpValue.Create(arr);
                return arr;
            }
            public override PhpArray AsArray(ref PhpValue me) { throw new InvalidCastException(); }
            public override string DisplayString(ref PhpValue me) => PhpVariable.TypeNameNull;
            public override void Accept(ref PhpValue me, PhpVariableVisitor visitor) => visitor.AcceptNull();
        }

        sealed class VoidTable : NullTable
        {
            public override PhpTypeCode Type => PhpTypeCode.Undefined;
            public override string DisplayString(ref PhpValue me) => PhpVariable.TypeNameVoid;
        }

        sealed class LongTable : TypeTable
        {
            public override PhpTypeCode Type => PhpTypeCode.Long;
            public override bool IsNull => false;
            public override object ToClass(ref PhpValue me) => new stdClass(me);	// new stdClass(){ $scalar = VALUE }
            public override string ToString(ref PhpValue me, Context ctx) => me.Long.ToString();
            public override string ToStringOrThrow(ref PhpValue me, Context ctx) => me.Long.ToString();
            public override long ToLong(ref PhpValue me) => me.Long;
            public override double ToDouble(ref PhpValue me) => (double)me.Long;
            public override bool ToBoolean(ref PhpValue me) => me.Long != 0;
            public override Convert.NumberInfo ToNumber(ref PhpValue me, out PhpNumber number)
            {
                number = PhpNumber.Create(me.Long);
                return Convert.NumberInfo.IsNumber | Convert.NumberInfo.LongInteger;
            }
            public override IntStringKey ToIntStringKey(ref PhpValue me) => new IntStringKey((int)me.Long);
            public override IPhpEnumerator GetForeachEnumerator(ref PhpValue me, bool aliasedValues, RuntimeTypeHandle caller) { throw new InvalidOperationException(); }
            public override int Compare(ref PhpValue me, PhpValue right) => Comparison.Compare(me.Long, right);
            public override bool StrictEquals(ref PhpValue me, PhpValue right) => right.TypeCode == PhpTypeCode.Long && right.Long == me.Long;
            public override object EnsureObject(ref PhpValue me) => PhpValue.FromClass(ToClass(ref me)); // me is not changed
            public override IPhpArray EnsureArray(ref PhpValue me) => new PhpArray(); // me is not changed
            public override PhpArray AsArray(ref PhpValue me) { throw new InvalidCastException(); }
            public override string DisplayString(ref PhpValue me) => me.Long.ToString();
            public override void Accept(ref PhpValue me, PhpVariableVisitor visitor) => visitor.Accept(me.Long);
        }

        sealed class DoubleTable : TypeTable
        {
            public override PhpTypeCode Type => PhpTypeCode.Double;
            public override bool IsNull => false;
            public override object ToClass(ref PhpValue me) => new stdClass(me);	// new stdClass(){ $scalar = VALUE }
            public override string ToString(ref PhpValue me, Context ctx) => Convert.ToString(me.Double, ctx);
            public override string ToStringOrThrow(ref PhpValue me, Context ctx) => Convert.ToString(me.Double, ctx);
            public override long ToLong(ref PhpValue me) => (long)me.Double;
            public override double ToDouble(ref PhpValue me) => me.Double;
            public override bool ToBoolean(ref PhpValue me) => me.Double != 0;
            public override Convert.NumberInfo ToNumber(ref PhpValue me, out PhpNumber number)
            {
                number = PhpNumber.Create(me.Double);
                return Convert.NumberInfo.IsNumber | Convert.NumberInfo.Double;
            }
            public override IntStringKey ToIntStringKey(ref PhpValue me) => new IntStringKey((int)me.Double);
            public override IPhpEnumerator GetForeachEnumerator(ref PhpValue me, bool aliasedValues, RuntimeTypeHandle caller) { throw new InvalidOperationException(); }
            public override int Compare(ref PhpValue me, PhpValue right) => Comparison.Compare(me.Double, right);
            public override bool StrictEquals(ref PhpValue me, PhpValue right) => right.TypeCode == PhpTypeCode.Double && right.Double == me.Double;
            public override object EnsureObject(ref PhpValue me) => PhpValue.FromClass(ToClass(ref me)); // me is not changed
            public override IPhpArray EnsureArray(ref PhpValue me) => new PhpArray(); // me is not changed
            public override PhpArray AsArray(ref PhpValue me) { throw new InvalidCastException(); }
            public override string DisplayString(ref PhpValue me) => me.Double.ToString();
            public override void Accept(ref PhpValue me, PhpVariableVisitor visitor) => visitor.Accept(me.Double);
        }

        sealed class BoolTable : TypeTable
        {
            public override PhpTypeCode Type => PhpTypeCode.Boolean;
            public override bool IsNull => false;
            public override object ToClass(ref PhpValue me) => new stdClass(me);	// new stdClass(){ $scalar = VALUE }
            public override string ToString(ref PhpValue me, Context ctx) => Convert.ToString(me.Boolean);
            public override string ToStringOrThrow(ref PhpValue me, Context ctx) => Convert.ToString(me.Boolean);
            public override long ToLong(ref PhpValue me) => me.Boolean ? 1L : 0L;
            public override double ToDouble(ref PhpValue me) => me.Boolean ? 1.0 : 0.0;
            public override bool ToBoolean(ref PhpValue me) => me.Boolean;
            public override Convert.NumberInfo ToNumber(ref PhpValue me, out PhpNumber number)
            {
                number = PhpNumber.Create(me.Boolean ? 1L : 0L);
                return Convert.NumberInfo.IsNumber | Convert.NumberInfo.LongInteger;
            }
            public override IntStringKey ToIntStringKey(ref PhpValue me) => new IntStringKey(me.Boolean ? 1 : 0);
            public override IPhpEnumerator GetForeachEnumerator(ref PhpValue me, bool aliasedValues, RuntimeTypeHandle caller) { throw new InvalidOperationException(); }
            public override int Compare(ref PhpValue me, PhpValue right) => Comparison.Compare(me.Boolean, right);
            public override bool StrictEquals(ref PhpValue me, PhpValue right) => right.TypeCode == PhpTypeCode.Boolean && right.Boolean == me.Boolean;
            public override object EnsureObject(ref PhpValue me)
            {
                var obj = new stdClass();   // empty class
                
                // me is changed if me.Boolean == FALSE
                if (me.Boolean == false)
                    me = PhpValue.FromClass(obj);
                
                return obj;
            }
            public override IPhpArray EnsureArray(ref PhpValue me)
            {
                var arr = new PhpArray();

                // me is changed if me.Boolean == FALSE
                if (me.Boolean == false)
                    me = PhpValue.Create(arr);

                return arr;
            }
            public override PhpArray AsArray(ref PhpValue me) { throw new InvalidCastException(); }
            public override string DisplayString(ref PhpValue me) => me.Boolean ? PhpVariable.True : PhpVariable.False;
            public override void Accept(ref PhpValue me, PhpVariableVisitor visitor) => visitor.Accept(me.Boolean);
        }

        sealed class StringTable : TypeTable
        {
            public override PhpTypeCode Type => PhpTypeCode.String;
            public override bool IsNull => false;
            public override object ToClass(ref PhpValue me) => new stdClass(me);	// new stdClass(){ $scalar = VALUE }
            public override string ToString(ref PhpValue me, Context ctx) => me.String;
            public override string ToStringOrThrow(ref PhpValue me, Context ctx) => me.String;
            public override long ToLong(ref PhpValue me) => Convert.StringToLongInteger(me.String);
            public override double ToDouble(ref PhpValue me) => Convert.StringToDouble(me.String);
            public override bool ToBoolean(ref PhpValue me) => Convert.ToBoolean(me.String);
            public override Convert.NumberInfo ToNumber(ref PhpValue me, out PhpNumber number) => Convert.ToNumber(me.String, out number);
            public override IntStringKey ToIntStringKey(ref PhpValue me) => new IntStringKey(me.String);
            public override IPhpEnumerator GetForeachEnumerator(ref PhpValue me, bool aliasedValues, RuntimeTypeHandle caller) { throw new NotImplementedException(); }
            public override int Compare(ref PhpValue me, PhpValue right) => Comparison.Compare(me.String, right);
            public override bool StrictEquals(ref PhpValue me, PhpValue right)
            {
                if (right.TypeCode == PhpTypeCode.String)
                    return right.String == me.String;

                if (right.TypeCode == PhpTypeCode.WritableString)
                    return right.WritableString.ToString() == me.String;

                return false;
            }
            public override object EnsureObject(ref PhpValue me)
            {
                var obj = ToClass(ref me);

                // me is changed if value is empty
                if (string.IsNullOrEmpty(me.String))
                    me = PhpValue.FromClass(obj);

                return obj;
            }
            public override IPhpArray EnsureArray(ref PhpValue me)
            {
                var arr = new PhpString(me.String);

                // me is changed if value is empty
                if (string.IsNullOrEmpty(me.String))
                    me = PhpValue.Create(arr);

                return arr;
            }
            public override PhpArray AsArray(ref PhpValue me) { throw new NotImplementedException(); }    // TODO: StringArray helper
            public override IPhpCallable AsCallable(ref PhpValue me) => PhpCallback.Create(me.String);
            public override string DisplayString(ref PhpValue me) => $"'{me.String}'";
            public override void Accept(ref PhpValue me, PhpVariableVisitor visitor) => visitor.Accept(me.String);
        }

        sealed class WritableStringTable : TypeTable
        {
            public override PhpTypeCode Type => PhpTypeCode.WritableString;
            public override bool IsNull => false;
            public override object ToClass(ref PhpValue me) => new stdClass(DeepCopy(ref me));	// new stdClass(){ $scalar = VALUE }
            public override string ToString(ref PhpValue me, Context ctx) => me.WritableString.ToString(ctx);
            public override string ToStringOrThrow(ref PhpValue me, Context ctx) => me.WritableString.ToStringOrThrow(ctx);
            public override long ToLong(ref PhpValue me) => me.WritableString.ToLong();
            public override double ToDouble(ref PhpValue me) => me.WritableString.ToDouble();
            public override bool ToBoolean(ref PhpValue me) => me.WritableString.ToBoolean();
            public override Convert.NumberInfo ToNumber(ref PhpValue me, out PhpNumber number) => me.WritableString.ToNumber(out number);
            public override IntStringKey ToIntStringKey(ref PhpValue me) => new IntStringKey(me.WritableString.ToString());
            public override IPhpEnumerator GetForeachEnumerator(ref PhpValue me, bool aliasedValues, RuntimeTypeHandle caller) { throw new NotImplementedException(); }
            public override int Compare(ref PhpValue me, PhpValue right) => Comparison.Compare(me.WritableString.ToString(), right);
            public override bool StrictEquals(ref PhpValue me, PhpValue right)
            {
                if (right.TypeCode == PhpTypeCode.String)
                    return right.String == me.WritableString.ToString();

                if (right.TypeCode == PhpTypeCode.WritableString)
                    return right.WritableString.ToString() == me.WritableString.ToString();

                return false;
            }
            public override object EnsureObject(ref PhpValue me)
            {
                //var obj = PhpValue.Create(new stdClass(ctx));
                //if (me.WritableString.IsEmpty)
                //{
                //    // me is changed if value is empty
                //    me = obj;
                //}
                //return obj;
                throw new NotImplementedException();
            }
            public override IPhpArray EnsureArray(ref PhpValue me) => me.WritableString;
            public override PhpValue DeepCopy(ref PhpValue me)
            {
                //me.WritableString.DeepCopy()
                throw new NotImplementedException();
            }
            public override PhpArray AsArray(ref PhpValue me) { throw new NotImplementedException(); }    // TODO: StringArray helper
            public override IPhpCallable AsCallable(ref PhpValue me) => PhpCallback.Create(me.WritableString.ToString());
            public override string DisplayString(ref PhpValue me) => $"'{me.WritableString.ToString()}'";
            public override void Accept(ref PhpValue me, PhpVariableVisitor visitor) => visitor.Accept(me.WritableString);
        }

        sealed class ClassTable : TypeTable
        {
            public override PhpTypeCode Type => PhpTypeCode.Object;
            public override bool IsNull => false;
            public override bool IsEmpty(ref PhpValue me) => false;
            public override object ToClass(ref PhpValue me) => me.Object;
            public override string ToString(ref PhpValue me, Context ctx)
            {
                if (me.Object is IPhpConvertible)
                {
                    return ((IPhpConvertible)me.Object).ToString(ctx);
                }
                else
                {
                    // TODO: magic function __toString()

                    //
                    return me.Object.ToString();
                }
            }
            public override string ToStringOrThrow(ref PhpValue me, Context ctx)
            {
                if (me.Object is IPhpConvertible) return ((IPhpConvertible)me.Object).ToStringOrThrow(ctx);
                throw new NotImplementedException();
            }
            public override long ToLong(ref PhpValue me)
            {
                if (me.Object is IPhpConvertible) return ((IPhpConvertible)me.Object).ToLong();
                throw new NotImplementedException();
            }
            public override double ToDouble(ref PhpValue me)
            {
                if (me.Object is IPhpConvertible) return ((IPhpConvertible)me.Object).ToDouble();
                throw new NotImplementedException();
            }
            public override bool ToBoolean(ref PhpValue me) => Convert.ToBoolean(me.Object);
            public override Convert.NumberInfo ToNumber(ref PhpValue me, out PhpNumber number)
            {
                if (me.Object is IPhpConvertible) return ((IPhpConvertible)me.Object).ToNumber(out number);
                throw new NotImplementedException();
            }
            public override IntStringKey ToIntStringKey(ref PhpValue me) { throw new NotImplementedException(); }
            public override IPhpEnumerator GetForeachEnumerator(ref PhpValue me, bool aliasedValues, RuntimeTypeHandle caller) { throw new NotImplementedException(); }
            public override int Compare(ref PhpValue me, PhpValue right)
            {
                if (me.Object.Equals(right._obj.Obj)) return 0;
                if (me.Object is IPhpComparable) return ((IPhpComparable)me.Object).Compare(right);
                if (right._obj.Obj is IPhpComparable) return - ((IPhpComparable)right._obj.Obj).Compare(me);

                throw new ArgumentException("incomparable_objects_compared_exception");
            }
            public override bool StrictEquals(ref PhpValue me, PhpValue right) => right.TypeCode == PhpTypeCode.Object && right.Object == me.Object;
            public override object EnsureObject(ref PhpValue me) => me.Object;
            public override IPhpArray EnsureArray(ref PhpValue me)
            {
                throw new NotImplementedException();  // Fatal Error: Cannot use object of type stdClass as array
            }
            public override PhpArray AsArray(ref PhpValue me) { throw new NotImplementedException(); }
            public override object AsObject(ref PhpValue me) => me.Object;
            public override IPhpCallable AsCallable(ref PhpValue me)
            {
                var obj = me.Object;

                if (obj is IPhpCallable) return (IPhpCallable)obj;
                
                throw new NotImplementedException();    // return PhpCallback.Create(obj);
            }
            public override string DisplayString(ref PhpValue me) => me.Object.GetType().FullName.Replace('.', '\\') + "#" + me.Object.GetHashCode().ToString("X");
            public override void Accept(ref PhpValue me, PhpVariableVisitor visitor) => visitor.AcceptObject(me.Object);
        }

        sealed class ArrayTable : TypeTable
        {
            public override PhpTypeCode Type => PhpTypeCode.PhpArray;
            public override bool IsNull => false;
            public override bool IsEmpty(ref PhpValue me) => me.Array.Count == 0;
            public override object ToClass(ref PhpValue me) => me.Array.ToClass();
            public override string ToString(ref PhpValue me, Context ctx) => me.Array.ToString(ctx);
            public override string ToStringOrThrow(ref PhpValue me, Context ctx) => me.Array.ToStringOrThrow(ctx);
            public override long ToLong(ref PhpValue me) => me.Array.ToLong();
            public override double ToDouble(ref PhpValue me) => me.Array.ToDouble();
            public override bool ToBoolean(ref PhpValue me) => me.Array.ToBoolean();
            public override Convert.NumberInfo ToNumber(ref PhpValue me, out PhpNumber number) => me.Array.ToNumber(out number);
            public override IntStringKey ToIntStringKey(ref PhpValue me) { throw new NotImplementedException(); }
            public override IPhpEnumerator GetForeachEnumerator(ref PhpValue me, bool aliasedValues, RuntimeTypeHandle caller) => me.Array.GetForeachEnumerator(aliasedValues);
            public override int Compare(ref PhpValue me, PhpValue right) => me.Array.Compare(right);
            public override bool StrictEquals(ref PhpValue me, PhpValue right)
            {
                throw new NotImplementedException();
            }
            public override object EnsureObject(ref PhpValue me) => ToClass(ref me);    // me is not modified
            public override IPhpArray EnsureArray(ref PhpValue me) => me.Array;
            public override PhpValue DeepCopy(ref PhpValue me) => PhpValue.Create(me.Array.DeepCopy());
            public override PhpArray AsArray(ref PhpValue me) => me.Array;
            public override IPhpCallable AsCallable(ref PhpValue me)
            {
                if (me.Array.Count == 2)
                {
                    PhpValue a, b;

                    var e = me.Array.GetFastEnumerator();
                    e.MoveNext();
                    a = e.CurrentValue;
                    e.MoveNext();
                    b = e.CurrentValue;

                    // [ class => object|string, methodname => string ]
                    return PhpCallback.Create(a, b);
                }
                else
                {
                    return base.AsCallable(ref me);
                }
            }
            public override string DisplayString(ref PhpValue me) => $"array(length = {me.Array.Count})";
            public override void Accept(ref PhpValue me, PhpVariableVisitor visitor) => visitor.Accept(me.Array);
        }

        sealed class AliasTable : TypeTable
        {
            public override PhpTypeCode Type => PhpTypeCode.Alias;
            public override bool IsNull => false;
            public override bool IsEmpty(ref PhpValue me) => me.Alias.Value.IsEmpty;
            public override object ToClass(ref PhpValue me) => me.Alias.ToClass();
            public override string ToString(ref PhpValue me, Context ctx) => me.Alias.ToString(ctx);
            public override string ToStringOrThrow(ref PhpValue me, Context ctx) => me.Alias.ToStringOrThrow(ctx);
            public override long ToLong(ref PhpValue me) => me.Alias.ToLong();
            public override double ToDouble(ref PhpValue me) => me.Alias.ToDouble();
            public override bool ToBoolean(ref PhpValue me) => me.Alias.ToBoolean();
            public override Convert.NumberInfo ToNumber(ref PhpValue me, out PhpNumber number) => me.Alias.ToNumber(out number);
            public override IntStringKey ToIntStringKey(ref PhpValue me) => me.Alias.Value.ToIntStringKey();
            public override IPhpEnumerator GetForeachEnumerator(ref PhpValue me, bool aliasedValues, RuntimeTypeHandle caller) => me.Alias.Value.GetForeachEnumerator(aliasedValues, caller);
            public override int Compare(ref PhpValue me, PhpValue right) => me.Alias.Value.Compare(right);
            public override bool StrictEquals(ref PhpValue me, PhpValue right) => me.Alias.Value.StrictEquals(right);
            public override object EnsureObject(ref PhpValue me) => me.Alias.Value.EnsureObject();
            public override IPhpArray EnsureArray(ref PhpValue me) => me.Alias.Value.EnsureArray();
            public override PhpAlias EnsureAlias(ref PhpValue me) => me.Alias;
            public override PhpArray AsArray(ref PhpValue me) => me.Alias.Value.AsArray();
            public override object AsObject(ref PhpValue me) => me.Alias.Value.AsObject();
            public override IPhpCallable AsCallable(ref PhpValue me) => me.Alias.Value.AsCallable();
            public override string DisplayString(ref PhpValue me) => "&" + me.Alias.Value.DisplayString;
            public override void Accept(ref PhpValue me, PhpVariableVisitor visitor) => visitor.Accept(me.Alias);
        }
    }
}
