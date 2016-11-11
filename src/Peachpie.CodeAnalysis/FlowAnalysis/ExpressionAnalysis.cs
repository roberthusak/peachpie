﻿using Devsense.PHP.Syntax;
using Devsense.PHP.Syntax.Ast;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.CodeGen;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
using Pchp.CodeAnalysis.Symbols;
using Roslyn.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pchp.CodeAnalysis.FlowAnalysis
{
    /// <summary>
    /// Visits single expressions and project transformations to flow state.
    /// </summary>
    internal class ExpressionAnalysis : AnalysisVisitor
    {
        #region Fields

        readonly ISemanticModel _model;

        /// <summary>
        /// Reference to corresponding source routine.
        /// </summary>
        protected SourceRoutineSymbol Routine => State.Routine;

        #endregion

        #region Helpers

        /// <summary>
        /// Gets value indicating the given type represents a double and nothing else.
        /// </summary>
        bool IsDoubleOnly(TypeRefMask tmask)
        {
            return tmask.IsSingleType && this.TypeCtx.IsDouble(tmask);
        }

        /// <summary>
        /// Gets value indicating the given type represents a double and nothing else.
        /// </summary>
        bool IsDoubleOnly(BoundExpression x) => IsDoubleOnly(x.TypeRefMask);

        /// <summary>
        /// Gets value indicating the given type represents a long and nothing else.
        /// </summary>
        bool IsLongOnly(TypeRefMask tmask)
        {
            return tmask.IsSingleType && this.TypeCtx.IsLong(tmask);
        }

        /// <summary>
        /// Gets value indicating the given type represents a long and nothing else.
        /// </summary>
        bool IsLongOnly(BoundExpression x) => IsLongOnly(x.TypeRefMask);

        /// <summary>
        /// Gets value indicating the given type is long or double or both but nothing else.
        /// </summary>
        /// <param name="tmask"></param>
        /// <returns></returns>
        bool IsNumberOnly(TypeRefMask tmask)
        {
            if (TypeCtx.IsLong(tmask) || TypeCtx.IsDouble(tmask))
            {
                if (tmask.IsSingleType)
                    return true;

                return !tmask.IsAnyType && TypeCtx.GetTypes(tmask)
                    .All(t => t.TypeCode == Core.PhpTypeCode.Long || t.TypeCode == Core.PhpTypeCode.Double);
            }

            return false;
        }

        /// <summary>
        /// Gets value indicating the given type represents only class types.
        /// </summary>
        bool IsClassOnly(TypeRefMask tmask)
        {
            return !tmask.IsVoid && !tmask.IsAnyType && TypeCtx.GetTypes(tmask).All(x => x.IsObject);
        }

        /// <summary>
        /// Gets value indicating the given type represents only array types.
        /// </summary>
        bool IsArrayOnly(TypeRefMask tmask)
        {
            return !tmask.IsVoid && !tmask.IsAnyType && TypeCtx.GetTypes(tmask).All(x => x.IsArray);
        }

        /// <summary>
        /// Gets value indicating the given type is long or double or both but nothing else.
        /// </summary>
        bool IsNumberOnly(BoundExpression x) => IsNumberOnly(x.TypeRefMask);

        /// <summary>
        /// Determines if given expression represents a variable which value is less than <c>Int64.Max</c> in current state.
        /// </summary>
        bool IsLTInt64Max(BoundReferenceExpression r)
        {
            var varname = AsVariableName(r);
            return varname != null && State.IsLTInt64Max(varname);
        }

        /// <summary>
        /// In case of a local variable or parameter, sets associated flag determining its value is less than Int64.Max.
        /// </summary>
        void LTInt64Max(BoundReferenceExpression r, bool lt)
        {
            var varname = AsVariableName(r);
            if (varname != null)
                State.LTInt64Max(varname, lt);
        }

        /// <summary>
        /// In case of a local variable or parameter, gets its name.
        /// </summary>
        string AsVariableName(BoundReferenceExpression r)
        {
            var vr = r as BoundVariableRef;
            if (vr != null && (vr.Variable is BoundLocal || vr.Variable is BoundParameter))
            {
                return vr.Variable.Name;
            }
            return null;
        }

        bool IsLongConstant(BoundExpression expr, long value)
        {
            if (expr.ConstantValue.HasValue)
            {
                if (expr.ConstantValue.Value is long) return ((long)expr.ConstantValue.Value) == value;
                if (expr.ConstantValue.Value is int) return ((int)expr.ConstantValue.Value) == value;
            }
            return false;
        }

        bool BindConstantValue(BoundExpression target, FieldSymbol symbol)
        {
            if (symbol != null && symbol.IsConst)
            {
                var cvalue = symbol.GetConstantValue(false);
                target.ConstantValue = (cvalue != null) ? new Optional<object>(cvalue.Value) : null;
                target.TypeRefMask = TypeRefFactory.CreateMask(TypeCtx, symbol.Type);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Updates the expression access and visits it.
        /// </summary>
        /// <param name="x">The expression.</param>
        /// <param name="access">New access.</param>
        void Visit(BoundExpression x, BoundAccess access)
        {
            x.Access = access;
            Accept(x);
        }

        internal TypeSymbol ResolveType(INamedTypeRef dtype)
        {
            return (TypeSymbol)_model.GetType(dtype.ClassName);
        }

        #endregion

        #region Construction

        public ExpressionAnalysis(Worklist<BoundBlock> worklist, ISemanticModel model)
            : base(worklist)
        {
            Contract.ThrowIfNull(model);
            _model = model;
        }

        #endregion

        #region Declaration Statements

        public override void VisitStaticStatement(BoundStaticVariableStatement x)
        {
            foreach (var v in x.Variables)
            {
                var name = v.Variable.Name;

                State.SetVarKind(new VariableName(name), VariableKind.StaticVariable);

                var oldtype = State.GetVarType(name);

                // set var
                if (v.InitialValue != null)
                {
                    // analyse initializer
                    Accept((IPhpOperation)v.InitialValue);

                    State.SetVarInitialized(name);
                    State.LTInt64Max(name, (v.InitialValue.ConstantValue.HasValue && v.InitialValue.ConstantValue.Value is long && (long)v.InitialValue.ConstantValue.Value < long.MaxValue));
                    State.SetVar(name, ((BoundExpression)v.InitialValue).TypeRefMask | oldtype);
                }
                else
                {
                    State.LTInt64Max(name, false);
                }
            }
        }

        public override void VisitGlobalStatement(BoundGlobalVariableStatement x)
        {
            foreach (var v in x.Variables)
            {
                State.SetVarKind(new VariableName(v.Name), VariableKind.GlobalVariable);
                State.SetVar(v.Name, TypeRefMask.AnyType);
                State.SetVarInitialized(v.Name);
            }
        }

        #endregion

        #region Visit Literals

        public override void VisitLiteral(BoundLiteral x)
        {
            x.TypeRefMask = x.ResolveTypeMask(TypeCtx);
        }

        #endregion

        #region Visit Assignments

        public override void VisitAssign(BoundAssignEx x)
        {
            Debug.Assert(x.Target.Access.IsWrite);
            Debug.Assert(x.Value.Access.IsRead);

            Accept(x.Value);

            // keep WriteRef flag
            var targetaccess = BoundAccess.Write;
            if (x.Target.Access.IsWriteRef)
                targetaccess = targetaccess.WithWriteRef(0);

            // new target access with resolved target type
            Visit(x.Target, targetaccess.WithWrite(x.Value.TypeRefMask));

            //
            x.TypeRefMask = x.Value.TypeRefMask;
        }

        static Operations CompoundOpToBinaryOp(Operations op)
        {
            switch (op)
            {
                case Operations.AssignAdd: return Operations.Add;
                case Operations.AssignAnd: return Operations.And;
                case Operations.AssignAppend: return Operations.Concat;
                case Operations.AssignDiv: return Operations.Div;
                case Operations.AssignMod: return Operations.Mod;
                case Operations.AssignMul: return Operations.Mul;
                case Operations.AssignOr: return Operations.Or;
                case Operations.AssignPow: return Operations.Pow;
                case Operations.AssignPrepend: return Operations.Concat;
                case Operations.AssignShiftLeft: return Operations.ShiftLeft;
                case Operations.AssignShiftRight: return Operations.ShiftRight;
                case Operations.AssignSub: return Operations.Sub;
                case Operations.AssignXor: return Operations.Xor;
                default:
                    throw ExceptionUtilities.UnexpectedValue(op);
            }
        }

        public override void VisitCompoundAssign(BoundCompoundAssignEx x)
        {
            Debug.Assert(x.Target.Access.IsRead && x.Target.Access.IsWrite);
            Debug.Assert(x.Value.Access.IsRead);

            // Target X Value
            var tmp = new BoundBinaryEx(x.Target.WithAccess(BoundAccess.Read), x.Value, CompoundOpToBinaryOp(x.Operation));
            Visit(tmp, ConditionBranch.AnyResult);

            // Target =
            Visit(x.Target, BoundAccess.Write.WithWrite(tmp.TypeRefMask));

            // put read access back
            x.Target.Access = x.Target.Access.WithRead();

            //
            x.TypeRefMask = tmp.TypeRefMask;
        }

        public override void VisitVariableRef(BoundVariableRef x)
        {
            if (x.Name.IsDirect)
            {
                // direct variable access:
                var name = x.Name.NameValue.Value;

                // bind variable place
                x.Variable = Routine.LocalsTable.BindVariable(x.Name.NameValue, State.GetVarKind(x.Name.NameValue));

                // update state
                if (x.Access.IsRead)
                {
                    State.SetVarUsed(name);
                    var vartype = State.GetVarType(name);

                    if (vartype.IsVoid || x.Variable.VariableKind == VariableKind.GlobalVariable)
                    {
                        // in global code or in case of undefined variable,
                        // assume the type is mixed (unspecified).
                        // In global code, the type of variable cannot be determined by type analysis, it can change between every two operations (this may be improved by better flow analysis).
                        vartype = TypeRefMask.AnyType;
                    }

                    if (x.Access.IsEnsure)
                    {
                        if (x.Access.IsReadRef)
                        {
                            State.SetVarRef(name);
                            vartype.IsRef = true;
                        }
                        if (x.Access.EnsureObject && !IsClassOnly(vartype))
                        {
                            vartype |= TypeCtx.GetSystemObjectTypeMask();
                        }
                        if (x.Access.EnsureArray && !IsArrayOnly(vartype))
                        {
                            vartype |= TypeCtx.GetArrayTypeMask();
                        }

                        State.SetVarInitialized(name);
                        State.SetVar(name, vartype);
                    }

                    x.TypeRefMask = vartype;
                }

                if (x.Access.IsWrite)
                {
                    //
                    State.SetVarInitialized(name);
                    State.SetVar(name, x.Access.WriteMask);
                    State.LTInt64Max(name, false);

                    x.TypeRefMask = x.Access.WriteMask;

                    if (x.Access.IsWriteRef)
                    {
                        State.SetVarRef(name);
                        x.TypeRefMask = x.TypeRefMask.WithRefFlag;
                    }

                    //
                    if (x.Variable.VariableKind == VariableKind.StaticVariable)
                    {
                        // analysis has to be started over // TODO: start from the block which declares the static local variable
                        var startBlock = Routine.ControlFlowGraph.Start;
                        var startState = startBlock.FlowState;

                        var oldVar = startState.GetVarType(name);
                        if (oldVar != x.TypeRefMask)
                        {
                            startState.SetVar(name, x.TypeRefMask);
                            this.Worklist.Enqueue(startBlock);
                        }
                    }
                }

                if (x.Access.IsUnset)
                {
                    State.SetVar(name, 0);
                    State.LTInt64Max(name, false);
                    x.TypeRefMask = 0;
                }
            }
            else
            {
                // indirect variable access:
                Routine.Flags |= RoutineFlags.HasIndirectVar;

                Accept(x.Name.NameExpression);

                // bind variable place
                if (x.Variable == null)
                {
                    x.Variable = new BoundIndirectLocal(x.Name.NameExpression);
                }

                // update state
                if (x.Access.IsRead)
                {
                    State.FlowContext.SetAllUsed();
                }

                if (x.Access.IsWrite)
                {
                    State.SetAllInitialized();
                }

                if (x.Access.IsUnset)
                {

                }

                return;
            }
        }

        public override void VisitIncDec(BoundIncDecEx x)
        {
            // <target> = <target> +/- 1L

            Debug.Assert(x.Access.IsRead || x.Access.IsNone);
            Debug.Assert(x.Target.Access.IsRead && x.Target.Access.IsWrite);

            Visit(x.Target, BoundAccess.Read);
            Visit(x.Value, BoundAccess.Read);

            Debug.Assert(IsNumberOnly(x.Value));    // 1L

            TypeRefMask resulttype;
            TypeRefMask sourcetype = x.Target.TypeRefMask;  // type of target before operation

            if (IsDoubleOnly(x.Target))
            {
                // double++ => double
                resulttype = TypeCtx.GetDoubleTypeMask();
            }
            else if (IsLTInt64Max(x.Target))    // we'd like to keep long if we are sure we don't overflow to double
            {
                // long++ [< long.MaxValue] => long
                resulttype = TypeCtx.GetLongTypeMask();
            }
            else
            {
                // long|double|anything++ => number
                resulttype = TypeCtx.GetNumberTypeMask();
            }

            Visit(x.Target, BoundAccess.Write.WithWrite(resulttype));

            //
            x.Target.Access = x.Target.Access.WithRead();   // put read access back to the target
            x.TypeRefMask = (x.IncrementKind == UnaryOperationKind.OperatorPrefixIncrement ||
                             x.IncrementKind == UnaryOperationKind.OperatorPrefixDecrement)
                            ? resulttype : sourcetype;
        }

        #endregion

        #region Visit BinaryEx

        private void VisitShortCircuitOp(BoundExpression lExpr, BoundExpression rExpr, bool isAndOp, ConditionBranch branch)
        {
            // Each operand has to be evaluated in various states and then the state merged.
            // Simulates short-circuit evaluation in runtime:

            var state = this.State; // original state

            if (branch == ConditionBranch.AnyResult)
            {
                if (isAndOp)
                {
                    // A == True && B == Any
                    // A == False

                    State = state.Clone();
                    VisitCondition(lExpr, ConditionBranch.ToTrue);
                    VisitCondition(rExpr, ConditionBranch.AnyResult);
                    var tmp = State;
                    State = state.Clone();
                    VisitCondition(lExpr, ConditionBranch.ToFalse);
                    State = State.Merge(tmp);
                }
                else
                {
                    // A == False && B == Any
                    // A == True

                    State = state.Clone();
                    VisitCondition(lExpr, ConditionBranch.ToFalse);
                    VisitCondition(rExpr, ConditionBranch.AnyResult);
                    var tmp = State;
                    State = state.Clone();
                    VisitCondition(lExpr, ConditionBranch.ToTrue);
                    State = State.Merge(tmp);
                }
            }
            else if (branch == ConditionBranch.ToTrue)
            {
                if (isAndOp)
                {
                    // A == True && B == True

                    VisitCondition(lExpr, ConditionBranch.ToTrue);
                    VisitCondition(rExpr, ConditionBranch.ToTrue);
                }
                else
                {
                    // A == False && B == True
                    // A == True

                    State = state.Clone();
                    VisitCondition(lExpr, ConditionBranch.ToFalse);
                    VisitCondition(rExpr, ConditionBranch.ToTrue);
                    var tmp = State;
                    State = state.Clone();
                    VisitCondition(lExpr, ConditionBranch.ToTrue);
                    State = State.Merge(tmp);
                }
            }
            else if (branch == ConditionBranch.ToFalse)
            {
                if (isAndOp)
                {
                    // A == True && B == False
                    // A == False

                    State = state.Clone();
                    VisitCondition(lExpr, ConditionBranch.ToTrue);
                    VisitCondition(rExpr, ConditionBranch.ToFalse);
                    var tmp = State;
                    State = state.Clone();
                    VisitCondition(lExpr, ConditionBranch.ToFalse);
                    State = State.Merge(tmp);
                }
                else
                {
                    // A == False && B == False

                    VisitCondition(lExpr, ConditionBranch.ToFalse);
                    VisitCondition(rExpr, ConditionBranch.ToFalse);
                }
            }
        }

        /// <summary>
        /// Gets resulting type of bit operation (bit or, and, xor).
        /// </summary>
        TypeRefMask GetBitOperationType(TypeRefMask lValType, TypeRefMask rValType)
        {
            TypeRefMask type;

            // type is string if both operands are string
            if ((lValType.IsAnyType && rValType.IsAnyType) ||
                (TypeCtx.IsAString(lValType) && TypeCtx.IsAString(rValType)))
            {
                type = TypeCtx.GetStringTypeMask();
            }
            else
            {
                type = default(TypeRefMask);
            }

            // type can be always long
            type |= TypeCtx.GetLongTypeMask();

            //
            return type;
        }

        /// <summary>
        /// Resolves value of bit operation.
        /// </summary>
        /// <remarks>TODO: move to **evaluation**.</remarks>
        object ResolveBitOperation(object xobj, object yobj, Operations op)
        {
            if (xobj is string && yobj is string)
            {
                throw new NotImplementedException();    // bit op of chars
            }

            var x = Core.PhpValue.FromClr(xobj);
            var y = Core.PhpValue.FromClr(yobj);
            long result;

            // TODO: use PhpValue overriden operators

            switch (op)
            {
                case Operations.BitOr: result = x.ToLong() | y.ToLong(); break;
                case Operations.BitAnd: result = x.ToLong() & y.ToLong(); break;
                case Operations.BitXor: result = x.ToLong() ^ y.ToLong(); break;
                default:
                    throw new ArgumentException(nameof(op));
            }

            if (result >= int.MinValue && result <= int.MaxValue)
            {
                return (int)result;
            }
            else
            {
                return result;
            }
        }

        /// <summary>
        /// Gets resulting type of <c>+</c> operation.
        /// </summary>
        TypeRefMask GetPlusOperationType(BoundExpression left, BoundExpression right)
        {
            var lValType = left.TypeRefMask;
            var rValType = right.TypeRefMask;

            // array + array => array
            // array + number => 0 (ERROR)
            // number + number => number
            // anytype + array => array
            // anytype + number => number

            var or = lValType | rValType;

            // double + number => double
            if (IsNumberOnly(or))
            {
                if (IsDoubleOnly(lValType) || IsDoubleOnly(rValType))
                    return TypeCtx.GetDoubleTypeMask();

                if (IsLTInt64Max(left as BoundReferenceExpression) && IsLongConstant(right, 1)) // LONG + 1, where LONG < long.MaxValue
                    return TypeCtx.GetLongTypeMask();

                return TypeCtx.GetNumberTypeMask();
            }

            //
            var type = TypeCtx.GetArraysFromMask(or);

            //
            if (or.IsAnyType || TypeCtx.IsNumber(or) || type == 0) // !this.TypeRefContext.IsArray(lValType & rValType))
                type |= TypeCtx.GetNumberTypeMask();    // anytype or an operand is number or operands are not a number nor both are not array

            if (or.IsAnyType)
                type |= TypeCtx.GetArrayTypeMask();

            //
            return type;
        }

        protected override void Visit(BoundBinaryEx x, ConditionBranch branch)
        {
            x.TypeRefMask = ResolveBinaryEx(x, branch);
        }

        TypeRefMask ResolveBinaryEx(BoundBinaryEx x, ConditionBranch branch)
        {
            if (x.Operation == Operations.And || x.Operation == Operations.Or)
            {
                this.VisitShortCircuitOp(x.Left, x.Right, x.Operation == Operations.And, branch);
            }
            else
            {
                Accept(x.Left);
                Accept(x.Right);
            }

            switch (x.Operation)
            {
                #region Arithmetic Operations

                case Operations.Add:
                    return GetPlusOperationType(x.Left, x.Right);

                case Operations.Sub:
                case Operations.Div:
                case Operations.Mul:
                case Operations.Pow:
                    if (IsDoubleOnly(x.Left.TypeRefMask) || IsDoubleOnly(x.Right.TypeRefMask)) // some operand is double and nothing else
                        return TypeCtx.GetDoubleTypeMask(); // double if we are sure about operands
                    return TypeCtx.GetNumberTypeMask();

                case Operations.Mod:
                    return TypeCtx.GetLongTypeMask();

                case Operations.ShiftLeft:
                case Operations.ShiftRight:
                    return TypeCtx.GetLongTypeMask();

                #endregion

                #region Boolean and Bitwise Operations

                case Operations.And:
                case Operations.Or:
                case Operations.Xor:
                    return TypeCtx.GetBooleanTypeMask();

                case Operations.BitAnd:
                case Operations.BitOr:
                case Operations.BitXor:

                    if (x.Left.ConstantValue.HasValue && x.Right.ConstantValue.HasValue)
                    {
                        x.ConstantValue = ResolveBitOperation(x.Left.ConstantValue.Value, x.Right.ConstantValue.Value, x.Operation);
                    }

                    return GetBitOperationType(x.Left.TypeRefMask, x.Right.TypeRefMask);    // int or string

                #endregion

                #region Comparing Operations

                case Operations.Equal:
                case Operations.NotEqual:
                case Operations.GreaterThan:
                case Operations.LessThan:
                case Operations.GreaterThanOrEqual:
                case Operations.LessThanOrEqual:
                case Operations.Identical:
                case Operations.NotIdentical:

                    if (branch == ConditionBranch.ToTrue)
                    {
                        if (x.Operation == Operations.LessThan && IsLongOnly(x.Right))
                            LTInt64Max(x.Left as BoundReferenceExpression, true);   // $x < LONG
                    }

                    return TypeCtx.GetBooleanTypeMask();

                #endregion

                case Operations.Concat:
                    return TypeCtx.GetWritableStringTypeMask();

                default:
                    throw ExceptionUtilities.Unreachable;
            }
        }

        #endregion

        #region Visit UnaryEx

        protected override void Visit(BoundUnaryEx x, ConditionBranch branch)
        {
            x.TypeRefMask = ResolveUnaryOperatorExpression(x, branch);
        }

        TypeRefMask ResolveUnaryOperatorExpression(BoundUnaryEx x, ConditionBranch branch)
        {
            //
            Accept(x.Operand);

            //
            switch (x.Operation)
            {
                case Operations.AtSign:
                    return x.Operand.TypeRefMask;

                case Operations.BitNegation:
                    return TypeCtx.GetLongTypeMask();   // TODO: or byte[]

                case Operations.Clone:
                    return x.Operand.TypeRefMask;

                case Operations.LogicNegation:
                    return TypeCtx.GetBooleanTypeMask();

                case Operations.Minus:
                    if (IsDoubleOnly(x.Operand))
                        return TypeCtx.GetDoubleTypeMask(); // double in case operand is double
                    return TypeCtx.GetNumberTypeMask();     // TODO: long in case operand is not a number

                case Operations.ObjectCast:
                    if (IsClassOnly(x.Operand.TypeRefMask))
                        return x.Operand.TypeRefMask;   // (object)<object>

                    return TypeCtx.GetSystemObjectTypeMask();   // TODO: return the exact type in case we know, return stdClass in case of a scalar

                case Operations.Plus:
                    if (IsNumberOnly(x.Operand.TypeRefMask))
                        return x.Operand.TypeRefMask;
                    return TypeCtx.GetNumberTypeMask();

                case Operations.Print:
                    return TypeCtx.GetLongTypeMask();

                case Operations.BoolCast:
                    return TypeCtx.GetBooleanTypeMask();

                case Operations.Int8Cast:
                case Operations.Int16Cast:
                case Operations.Int32Cast:
                case Operations.UInt8Cast:
                case Operations.UInt16Cast:
                // -->
                case Operations.UInt64Cast:
                case Operations.UInt32Cast:
                case Operations.Int64Cast:
                    return TypeCtx.GetLongTypeMask();

                case Operations.DecimalCast:
                case Operations.DoubleCast:
                case Operations.FloatCast:
                    return TypeCtx.GetDoubleTypeMask();

                case Operations.UnicodeCast: // TODO
                case Operations.StringCast:
                    return TypeCtx.GetStringTypeMask(); // binary | unicode | both

                case Operations.BinaryCast:
                    return TypeCtx.GetWritableStringTypeMask(); // binary string builder

                case Operations.ArrayCast:
                    return TypeCtx.GetArrayTypeMask();  // TODO: can we be more specific?

                case Operations.UnsetCast:
                    return TypeCtx.GetNullTypeMask();   // null

                default:
                    throw ExceptionUtilities.Unreachable;
            }
        }

        #endregion

        #region Visit InstanceOf

        protected override void Visit(BoundInstanceOfEx x, ConditionBranch branch)
        {
            Accept(x.Operand);
            VisitTypeRef(x.AsType);

            // TOOD: x.ConstantValue // in case we know and the operand is a local variable (we can ignore the expression and emit result immediatelly)

            if (branch == ConditionBranch.ToTrue && x.Operand is BoundVariableRef)
            {
                var vref = (BoundVariableRef)x.Operand;
                if (vref.Name.IsDirect)
                {
                    // if (Variable is T) => variable is T in True branch state
                    State.SetVar(vref.Name.NameValue.Value, TypeCtx.GetTypeMask(x.AsType.TypeRef, true));
                }
            }

            //
            x.TypeRefMask = TypeCtx.GetBooleanTypeMask();
        }

        #endregion

        #region Visit Function Call

        protected override void VisitRoutineCall(BoundRoutineCall x)
        {
            x.TypeRefMask = TypeRefMask.AnyType;

            // TODO: write arguments Access
            // TODO: visit invocation member of
            // TODO: 2 pass, analyze arguments -> resolve method -> assign argument to parameter -> write arguments access -> analyze arguments again

            // visit arguments:
            base.VisitRoutineCall(x);
        }

        void VisitRoutineCallEpilogue(BoundRoutineCall x)
        {
            //
            if (x.TargetMethod != null)
            {
                TypeRefMask result_type = 0;

                var args = x.ArgumentsInSourceOrder.Select(a => a.Value).ToImmutableArray();

                // reanalyse candidates
                foreach (var m in new[] { x.TargetMethod }) // TODO: all candidates
                {
                    // analyze TargetMethod with x.Arguments
                    // require method result type if access != none
                    var enqueued = this.Worklist.EnqueueRoutine(m, CurrentBlock, args);
                    if (enqueued)   // => target has to be reanalysed
                    {
                        // note: continuing current block may be waste of time
                    }

                    result_type |= m.GetResultType(TypeCtx);
                }

                x.TypeRefMask = result_type;
            }

            //
            if (x.Access.IsReadRef)
            {
                x.TypeRefMask = x.TypeRefMask.WithRefFlag;
            }
        }

        public override void VisitExit(BoundExitEx x)
        {
            VisitRoutineCall(x);
            VisitRoutineCallEpilogue(x);
        }

        public override void VisitEcho(BoundEcho x)
        {
            VisitRoutineCall(x);
            x.TypeRefMask = 0;
            VisitRoutineCallEpilogue(x);
        }

        public override void VisitConcat(BoundConcatEx x)
        {
            VisitRoutineCall(x);
            x.TypeRefMask = TypeCtx.GetWritableStringTypeMask();
            VisitRoutineCallEpilogue(x);
        }

        public override void VisitGlobalFunctionCall(BoundGlobalFunctionCall x)
        {
            Accept(x.Name.NameExpression);

            VisitRoutineCall(x);

            if (x.Name.IsDirect)
            {
                var candidates = _model.ResolveFunction(x.Name.NameValue).Cast<MethodSymbol>().ToArray();
                if (candidates.Length == 0 && x.NameOpt.HasValue)
                {
                    candidates = _model.ResolveFunction(x.NameOpt.Value).Cast<MethodSymbol>().ToArray();
                }

                var args = x.ArgumentsInSourceOrder.Select(a => a.Value.TypeRefMask).ToArray();
                x.TargetMethod = new OverloadsList(candidates).Resolve(this.TypeCtx, args, null);
            }

            VisitRoutineCallEpilogue(x);
        }

        public override void VisitInstanceFunctionCall(BoundInstanceFunctionCall x)
        {

            Accept(x.Instance);
            Accept(x.Name.NameExpression);

            VisitRoutineCall(x);

            if (x.Name.IsDirect)
            {
                var typeref = TypeCtx.GetTypes(x.Instance.TypeRefMask);
                if (typeref.Count > 1)
                {
                    // TODO: some common base ?
                }

                if (typeref.Count == 1)
                {
                    var classtype = typeref.Where(t => t.IsObject).AsImmutable().SingleOrDefault();
                    if (classtype != null)
                    {
                        var type = (NamedTypeSymbol)_model.GetType(classtype.QualifiedName);
                        if (type != null)
                        {
                            var candidates = type.LookupMethods(x.Name.NameValue.Name.Value);
                            var args = x.ArgumentsInSourceOrder.Select(a => a.Value.TypeRefMask).ToArray();
                            x.TargetMethod = new OverloadsList(candidates.ToArray()).Resolve(this.TypeCtx, args, this.TypeCtx.ContainingType);
                        }
                    }
                }
            }

            VisitRoutineCallEpilogue(x);
        }

        public override void VisitStaticFunctionCall(BoundStaticFunctionCall x)
        {
            VisitTypeRef(x.TypeRef);

            VisitRoutineCall(x);

            Accept(x.Name.NameExpression);

            if (x.Name.IsDirect && x.TypeRef.ResolvedType != null)
            {
                // TODO: resolve all candidates, visibility, static methods or instance on self/parent/static
                var candidates = x.TypeRef.ResolvedType.LookupMethods(x.Name.NameValue.Name.Value);
                // if (candidates.Any(c => c.HasThis)) throw new NotImplementedException("instance method called statically");

                var args = x.ArgumentsInSourceOrder.Select(a => a.Value.TypeRefMask).ToArray();
                x.TargetMethod = new OverloadsList(candidates.ToArray()).Resolve(this.TypeCtx, args, this.TypeCtx.ContainingType);
            }

            VisitRoutineCallEpilogue(x);
        }

        public override void VisitTypeRef(BoundTypeRef tref)
        {
            if (tref == null)
                return;

            if (tref.TypeRef is INamedTypeRef)
            {
                var qname = ((INamedTypeRef)tref.TypeRef).ClassName;
                if (qname.IsReservedClassName)
                {
                    if (qname.IsSelfClassName)
                    {
                        tref.ResolvedType = TypeCtx.ContainingType ?? new MissingMetadataTypeSymbol(qname.ToString(), 0, false);
                    }
                    else if (qname.IsParentClassName)
                    {
                        tref.ResolvedType = TypeCtx.ContainingType?.BaseType ?? new MissingMetadataTypeSymbol(qname.ToString(), 0, false);
                    }
                    else if (qname.IsStaticClassName)
                    {
                        this.Routine.Flags |= RoutineFlags.UsesLateStatic;

                        throw new NotImplementedException("Late static bound type.");
                    }
                }
                else
                {
                    tref.ResolvedType = (TypeSymbol)_model.GetType(qname);
                }
            }

            Accept(tref.TypeExpression);
        }

        public override void VisitNew(BoundNewEx x)
        {
            VisitTypeRef(x.TypeRef);

            // resolve target type
            var type = (NamedTypeSymbol)x.TypeRef.ResolvedType;
            if (type != null)
            {
                if (type.IsStatic)
                {
                    // TODO: Err cannot instantiate a static class
                }

                var candidates = type.InstanceConstructors.ToArray();

                //
                var args = x.ArgumentsInSourceOrder.Select(a => a.Value).ToImmutableArray();
                var argsType = args.Select(a => a.TypeRefMask).ToArray();

                x.TargetMethod = new OverloadsList(candidates).Resolve(this.TypeCtx, argsType, null);

                // reanalyse candidates
                foreach (var c in candidates)
                {
                    // analyze TargetMethod with x.Arguments
                    this.Worklist.EnqueueRoutine(c, CurrentBlock, args);
                }

                x.ResultType = type;
            }

            x.TypeRefMask = TypeCtx.GetTypeMask(x.TypeRef.TypeRef, false);
        }

        public override void VisitInclude(BoundIncludeEx x)
        {
            this.Routine.Flags |= RoutineFlags.HasInclude;

            // resolve target script
            Debug.Assert(x.ArgumentsInSourceOrder.Length == 1);
            var targetExpr = x.ArgumentsInSourceOrder[0].Value;

            //
            x.Target = null;

            if (targetExpr.ConstantValue.HasValue)
            {
                var value = targetExpr.ConstantValue.Value as string;
                if (value != null)
                {
                    var targetFile = _model.GetFile(value);
                    if (targetFile != null)
                    {
                        x.Target = targetFile.MainMethod;
                    }
                }
            }

            // resolve result type
            if (x.Access.IsRead)
            {
                var target = x.Target;
                if (target != null)
                {
                    x.ResultType = target.ReturnType;
                    x.TypeRefMask = target.GetResultType(TypeCtx);

                    if (x.IsOnceSemantic)
                    {
                        // include_once, require_once returns TRUE in case the script was already included
                        x.TypeRefMask |= TypeCtx.GetBooleanTypeMask();
                    }
                }
                else
                {
                    x.TypeRefMask = TypeRefMask.AnyType;
                }
            }
            else
            {
                x.TypeRefMask = 0;
            }
        }

        public override void VisitArgument(BoundArgument x)
        {
            if (x.Parameter != null)
            {
                // TODO: write arguments access
                // TODO: conversion by simplifier visitor
            }

            Accept(x.Value);
        }

        #endregion

        #region Visit FieldRef

        public override void VisitFieldRef(BoundFieldRef x)
        {
            Accept(x.Instance);
            VisitTypeRef(x.ParentType);
            Accept(x.FieldName.NameExpression);

            if (x.IsInstanceField)  // {Instance}->FieldName
            {
                Debug.Assert(x.Instance != null);
                Debug.Assert(x.Instance.Access.IsRead);

                // resolve field if possible
                var typerefs = TypeCtx.GetTypes(x.Instance.TypeRefMask);
                if (typerefs.Count == 1 && typerefs[0].IsObject)
                {
                    // TODO: x.Instance.ResultType instead of following

                    var t = (NamedTypeSymbol)_model.GetType(typerefs[0].QualifiedName);
                    if (t != null)
                    {
                        if (x.FieldName.IsDirect)
                        {
                            // TODO: visibility and resolution (model)

                            var field = t.LookupMember<FieldSymbol>(x.FieldName.NameValue.Value);
                            if (field != null)
                            {
                                x.BoundReference = new BoundFieldPlace(x.Instance, field, x);
                                x.TypeRefMask = field.GetResultType(TypeCtx);
                                return;
                            }
                            else
                            {
                                var prop = t.LookupMember<PropertySymbol>(x.FieldName.NameValue.Value);
                                if (prop != null)
                                {
                                    x.BoundReference = new BoundPropertyPlace(x.Instance, prop);
                                    x.TypeRefMask = TypeRefFactory.CreateMask(TypeCtx, prop.Type);
                                    return;
                                }
                                else
                                {
                                    // TODO: use runtime fields directly, __get, __set, etc.,
                                    // do not fallback to BoundIndirectFieldPlace
                                }
                            }
                        }
                    }

                }

                // dynamic behavior
                // indirect field access ...

                x.BoundReference = new BoundIndirectFieldPlace(x.Instance, x.FieldName, x.Access);
                x.TypeRefMask = TypeRefMask.AnyType;
                return;
            }

            // static fields or constants
            if (x.IsStaticField || x.IsClassConstant)    // {ClassName}::${StaticFieldName}, {ClassName}::{ConstantName}
            {
                var ParentType = (NamedTypeSymbol)x.ParentType.ResolvedType;

                if (x.IsClassConstant)
                {
                    Debug.Assert(x.Access.IsRead);
                    Debug.Assert(!x.Access.IsEnsure && !x.Access.IsWrite && !x.Access.IsReadRef);
                }

                if (ParentType != null && x.FieldName.IsDirect)
                {
                    var fldname = x.FieldName.NameValue.Value;
                    var field = x.IsStaticField ? ParentType.ResolveStaticField(fldname) : ParentType.ResolveClassConstant(fldname);
                    if (field != null)
                    {
                        // TODO: visibility -> ErrCode

                        Debug.Assert(
                            field.IsConst ||    // .NET constant
                            field.IsStatic ||   // .NET static
                            field.ContainingType.TryGetStatics().LookupMember<FieldSymbol>(x.FieldName.NameValue.Value) != null); // or PHP context static

                        if (BindConstantValue(x, field))
                        {
                            Debug.Assert(x.Access.IsRead && !x.Access.IsWrite && !x.Access.IsEnsure);
                            x.BoundReference = null; // not reachable
                        }
                        else
                        {
                            x.BoundReference = field.IsStatic
                                ? new BoundFieldPlace(null, field, x)        // the field is real .NET static member (CLR static fields)
                                : new BoundPhpStaticFieldPlace(field, x);    // the field is contained in special __statics container (fields & constants)
                        }

                        x.TypeRefMask = field.GetResultType(TypeCtx);
                        return;
                    }
                    else if (x.IsStaticField)
                    {
                        // TODO: visibility
                        var prop = ParentType.LookupMember<PropertySymbol>(fldname);
                        if (prop != null && prop.IsStatic)
                        {
                            x.BoundReference = new BoundPropertyPlace(null, prop);
                            x.TypeRefMask = TypeRefFactory.CreateMask(TypeCtx, prop.Type);
                            return;
                        }
                    }

                    // TODO: __getStatic, __setStatic
                }

                // indirect field access:
                // indirect field access with known class name:
                x.BoundReference = new BoundIndirectStFieldPlace(x.ParentType, x.FieldName, x);
                x.TypeRefMask = TypeRefMask.AnyType;
                return;
            }
        }

        #endregion

        #region Visit ArrayEx, ArrayItemEx

        public override void VisitArray(BoundArrayEx x)
        {
            var items = x.Items;
            TypeRefMask elementType = 0;

            // analyse elements
            foreach (var i in items)
            {
                Accept(i.Key);
                Accept(i.Value);

                elementType |= i.Value.TypeRefMask;
            }

            // writeup result type
            x.TypeRefMask = elementType.IsVoid
                ? TypeCtx.GetArrayTypeMask()
                : TypeCtx.GetArrayTypeMask(elementType);
        }

        public override void VisitArrayItem(BoundArrayItemEx x)
        {
            Accept(x.Array);
            Accept(x.Index);

            //
            x.TypeRefMask = x.Access.IsReadRef
                ? TypeRefMask.AnyType.WithRefFlag
                : TypeRefMask.AnyType;
        }

        #endregion

        #region Visit

        public override void VisitIsEmpty(BoundIsEmptyEx x)
        {
            Accept(x.Variable);
            x.TypeRefMask = TypeCtx.GetBooleanTypeMask();
        }

        public override void VisitIsSet(BoundIsSetEx x)
        {
            x.VarReferences.ForEach(Accept);
            x.TypeRefMask = TypeCtx.GetBooleanTypeMask();
        }

        public override void VisitUnset(BoundUnset x)
        {
            x.VarReferences.ForEach(Accept);
        }

        public override void VisitList(BoundListEx x)
        {
            var elementtype = this.TypeCtx.GetElementType(x.Access.WriteMask);
            Debug.Assert(!elementtype.IsVoid);

            foreach (var v in x.Variables.WhereNotNull())   // list() may contain NULL implying ignored variable
            {
                Visit(v, v.Access.WithWrite(elementtype));
            }
        }

        public override void VisitPseudoConstUse(BoundPseudoConst x)
        {
            object value = null;

            switch (x.Type)
            {
                case PseudoConstUse.Types.Line:
                    value = TypeCtx.SourceUnit.GetLineFromPosition(x.PhpSyntax.Span.Start) + 1;
                    break;

                case PseudoConstUse.Types.Class:
                case PseudoConstUse.Types.Trait:
                    value = (TypeCtx.ContainingType is IPhpTypeSymbol)
                        ? ((IPhpTypeSymbol)TypeCtx.ContainingType).FullName.ToString()
                        : string.Empty;
                    break;

                case PseudoConstUse.Types.Method:
                    value = Routine != null
                        ? TypeCtx.ContainingType is IPhpTypeSymbol
                            ? ((IPhpTypeSymbol)TypeCtx.ContainingType).FullName.ToString(new Name(Routine.Name), false)
                            : Routine.Name
                        : string.Empty;
                    break;

                case PseudoConstUse.Types.Function:
                    value = Routine != null ? Routine.RoutineName : string.Empty;
                    break;

                case PseudoConstUse.Types.Namespace:
                    value = (Naming != null && Naming.CurrentNamespace.HasValue)
                        ? Naming.CurrentNamespace.Value.NamespacePhpName
                        : string.Empty;
                    break;

                case PseudoConstUse.Types.Dir:
                case PseudoConstUse.Types.File:
                    x.TypeRefMask = TypeCtx.GetStringTypeMask();
                    return;

                default:
                    throw ExceptionUtilities.UnexpectedValue(x.Type);
            }

            Debug.Assert(value != null);    // pseudoconstant has been set

            x.ConstantValue = new Optional<object>(value);

            if (value is string) x.TypeRefMask = TypeCtx.GetStringTypeMask();
            else if (value is int || value is long) x.TypeRefMask = TypeCtx.GetLongTypeMask();
            else throw ExceptionUtilities.UnexpectedValue(value);
        }

        public override void VisitGlobalConstUse(BoundGlobalConst x)
        {
            // TODO: check constant name

            // bind to app-wide constant if possible
            var constant = (FieldSymbol)_model.ResolveConstant(x.Name);
            if (!BindConstantValue(x, constant))
            {
                if (constant != null && constant.IsStatic && constant.IsReadOnly)
                {
                    x._boundExpressionOpt = new BoundFieldPlace(null, constant, x);
                    x.TypeRefMask = constant.GetResultType(TypeCtx);
                }
                else
                {
                    x.TypeRefMask = TypeRefMask.AnyType;    // only scalars ?
                }
            }
        }

        public override void VisitConditional(BoundConditionalEx x)
        {
            var state = State;
            var trueExpr = x.IfTrue ?? x.Condition;

            // true branch
            var trueState = State = state.Clone();
            VisitCondition(x.Condition, ConditionBranch.ToTrue);
            Accept(trueExpr);

            // false branch
            var falseState = State = state.Clone();
            VisitCondition(x.Condition, ConditionBranch.ToFalse);
            Accept(x.IfFalse);

            // merge both states
            State = trueState.Merge(falseState);

            //
            x.TypeRefMask = trueExpr.TypeRefMask | x.IfFalse.TypeRefMask;
        }

        public override void VisitExpressionStatement(BoundExpressionStatement x)
        {
            Accept(x.Expression);
        }

        public override void VisitReturn(BoundReturnStatement x)
        {
            if (x.Returned != null)
            {
                Accept(x.Returned);
                State.FlowThroughReturn(x.Returned.TypeRefMask);
            }
        }

        public override void VisitThrow(BoundThrowStatement x)
        {
            Accept(x.Thrown);
        }

        #endregion
    }
}
