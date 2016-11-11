﻿using Devsense.PHP.Syntax.Ast;
using Devsense.PHP.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeGen;
using Microsoft.CodeAnalysis.Emit;
using Pchp.CodeAnalysis.Emit;
using Pchp.CodeAnalysis.FlowAnalysis;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
using Pchp.CodeAnalysis.Symbols;
using Roslyn.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Pchp.CodeAnalysis.CodeGen
{
    partial class CodeGenerator
    {
        /// <summary>
        /// Emits <c>context</c> onto the evaluation stack.
        /// </summary>
        public TypeSymbol EmitLoadContext()
        {
            if (_contextPlace == null)
                throw new InvalidOperationException("Context is not available.");

            return _contextPlace.EmitLoad(_il);
        }

        /// <summary>
        /// Gets <see cref="IPlace"/> of current <c>Context</c>.
        /// </summary>
        public IPlace ContextPlaceOpt => _contextPlace;

        public void EmitCallerRuntimeTypeHandle()
        {
            var caller = this.CallerType;
            if (caller != null)
            {
                // RuntimeTypeHandle
                EmitLoadToken(caller, null);
            }
            else
            {
                // default(RuntimeTypeHandle)
                EmitLoadDefaultOfValueType(this.CoreTypes.RuntimeTypeHandle);
            }
        }

        /// <summary>
        /// Emits reference to <c>$GLOBALS</c>.
        /// </summary>
        /// <returns>Type of <c>PhpArray</c></returns>
        public TypeSymbol EmitLoadGlobals()
        {
            // <ctx>.Globals
            EmitLoadContext();
            return EmitCall(ILOpCode.Call, CoreMethods.Context.get_Globals)
                .Expect(CoreTypes.PhpArray);
        }

        /// <summary>
        /// Emits reference to <c>$_SERVER</c>.
        /// </summary>
        /// <returns>Type of <c>PhpArray</c></returns>
        public TypeSymbol EmitLoadServer()
        {
            // <ctx>.Server
            EmitLoadContext();
            return EmitCall(ILOpCode.Call, CoreMethods.Context.get_Server)
                .Expect(CoreTypes.PhpArray);
        }

        /// <summary>
        /// Gets place referring to array of unoptimized local variables.
        /// Always valid in context of global scope.
        /// </summary>
        public IPlace LocalsPlaceOpt => _localsPlaceOpt;

        /// <summary>
        /// Gets value indicating the routine uses unoptimized locals access.
        /// This means, all the local variables are stored within an associative array instead of local slots.
        /// This value implicates, <see cref="LocalsPlaceOpt"/> is not <c>null</c>.
        /// </summary>
        public bool HasUnoptimizedLocals => LocalsPlaceOpt != null;

        /// <summary>
        /// Emits reference to <c>this</c>.
        /// </summary>
        /// <returns>Type of <c>this</c> in current context, pushed on top of the evaluation stack.</returns>
        public TypeSymbol EmitThis()
        {
            Contract.ThrowIfNull(_thisPlace);
            return EmitThisOrNull();
        }

        public TypeSymbol EmitThisOrNull()
        {
            if (_thisPlace == null)
            {
                _il.EmitNullConstant();
                return CoreTypes.Object;
            }
            else
            {
                return _thisPlace.EmitLoad(_il);
            }
        }

        /// <summary>
        /// If possible, based on type analysis, unwraps most specific type from give variable without a runtime type check.
        /// </summary>
        internal TypeSymbol TryEmitVariableSpecialize(BoundExpression expr)
        {
            Debug.Assert(expr.Access.IsRead);

            if (!expr.Access.IsEnsure)
            {
                // avoiding of load of full value if not necessary
                return TryEmitVariableSpecialize(PlaceOrNull(expr), expr.TypeRefMask);
            }
            else
            {
                // we have to call expr.Emit() to generate ensureness correctly (Ensure Object, Ensure Array, Read Alias)
                return null;
            }
        }

        /// <summary>
        /// If possible, based on type analysis, unwraps most specific type from give variable without a runtime type check.
        /// </summary>
        internal TypeSymbol TryEmitVariableSpecialize(IPlace place, TypeRefMask tmask)
        {
            if (place != null && tmask.IsSingleType && !tmask.IsRef)
            {
                if (place.HasAddress)
                {
                    if (place.TypeOpt == CoreTypes.PhpNumber)
                    {
                        // access directly without type checking
                        if (IsDoubleOnly(tmask))
                        {
                            place.EmitLoadAddress(_il);
                            return EmitCall(ILOpCode.Call, CoreMethods.PhpNumber.get_Double)
                                .Expect(SpecialType.System_Double);
                        }
                        else if (IsLongOnly(tmask))
                        {
                            place.EmitLoadAddress(_il);
                            return EmitCall(ILOpCode.Call, CoreMethods.PhpNumber.get_Long)
                                .Expect(SpecialType.System_Int64);
                        }
                    }
                    else if (place.TypeOpt == CoreTypes.PhpValue)
                    {
                        // access directly without type checking
                        if (IsDoubleOnly(tmask))
                        {
                            place.EmitLoadAddress(_il);
                            return EmitCall(ILOpCode.Call, CoreMethods.PhpValue.get_Double)
                                .Expect(SpecialType.System_Double);
                        }
                        else if (IsLongOnly(tmask))
                        {
                            place.EmitLoadAddress(_il);
                            return EmitCall(ILOpCode.Call, CoreMethods.PhpValue.get_Long)
                                .Expect(SpecialType.System_Int64);
                        }
                        else if (IsBooleanOnly(tmask))
                        {
                            place.EmitLoadAddress(_il);
                            return EmitCall(ILOpCode.Call, CoreMethods.PhpValue.get_Boolean)
                                .Expect(SpecialType.System_Boolean);
                        }
                        else if (IsReadonlyStringOnly(tmask))
                        {
                            place.EmitLoadAddress(_il);
                            return EmitCall(ILOpCode.Call, CoreMethods.PhpValue.get_String)
                                .Expect(SpecialType.System_String);
                        }
                        //else if (IsArrayOnly(tmask))
                        //{
                        //    place.EmitLoadAddress(_il);
                        //    return EmitCall(ILOpCode.Call, CoreMethods.PhpValue.get_Array)    NOTE!! PhpValue.Array is PhpArray
                        //        .Expect(CoreTypes.IPhpArray);
                        //}
                        else if (IsClassOnly(tmask))
                        {
                            place.EmitLoadAddress(_il);
                            EmitCall(ILOpCode.Call, CoreMethods.PhpValue.get_Object)
                                .Expect(SpecialType.System_Object);

                            // DEBUG:
                            //if (tmask.IsSingleType)
                            //{
                            //    var tref = this.Routine.TypeRefContext.GetTypes(tmask)[0];
                            //    var clrtype = (TypeSymbol)this.DeclaringCompilation.GlobalSemantics.GetType(tref.QualifiedName);
                            //    if (clrtype != null && !clrtype.IsErrorType())
                            //    {
                            //        this.EmitCastClass(clrtype);
                            //        return clrtype;
                            //    }
                            //}

                            return this.CoreTypes.Object;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// If possible, based on type analysis, unwraps more specific type from a value currently on stack without a runtime type check.
        /// </summary>
        /// <returns>New type on top of evaluation stack.</returns>
        internal TypeSymbol EmitSpecialize(BoundExpression expr)
        {
            // load resulting value directly if resolved:
            if (expr.ConstantValue.HasValue)
            {
                if (expr.Access.IsNone)
                {
                    return CoreTypes.Void;
                }

                if (expr.Access.IsRead)
                {
                    return EmitLoadConstant(expr.ConstantValue.Value, expr.Access.TargetType);
                }
            }

            //
            if (expr.Access.IsNone) // no need for specializing, the value won't be read anyway
            {
                return (expr.ResultType = expr.Emit(this));
            }
            else
            {
                Debug.Assert(expr.Access.IsRead);

                return expr.ResultType = (TryEmitVariableSpecialize(expr) ?? EmitSpecialize(expr.Emit(this), expr.TypeRefMask));
            }
        }

        /// <summary>
        /// If possible, based on type analysis, unwraps more specific type from a value currently on stack without a runtime type check.
        /// </summary>
        /// <param name="stack">Type of value currently on top of evaluationb stack.</param>
        /// <param name="tmask">Result of analysis what type will be there in runtime.</param>
        /// <returns>New type on top of evaluation stack.</returns>
        internal TypeSymbol EmitSpecialize(TypeSymbol stack, TypeRefMask tmask)
        {
            // specialize type if possible
            if (tmask.IsSingleType && !tmask.IsRef)
            {
                if (stack == this.CoreTypes.PhpNumber)
                {
                    if (IsDoubleOnly(tmask))
                    {
                        EmitPhpNumberAddr();
                        return EmitCall(ILOpCode.Call, this.CoreMethods.PhpNumber.get_Double)
                            .Expect(SpecialType.System_Double);
                    }
                    else if (IsLongOnly(tmask))
                    {
                        EmitPhpNumberAddr();
                        return EmitCall(ILOpCode.Call, this.CoreMethods.PhpNumber.get_Long)
                            .Expect(SpecialType.System_Int64);
                    }
                }
                else if (stack == CoreTypes.PhpValue)
                {
                    // access directly without type checking
                    if (IsDoubleOnly(tmask))
                    {
                        EmitPhpValueAddr();
                        return EmitCall(ILOpCode.Call, CoreMethods.PhpValue.get_Double)
                            .Expect(SpecialType.System_Double);
                    }
                    else if (IsLongOnly(tmask))
                    {
                        EmitPhpValueAddr();
                        return EmitCall(ILOpCode.Call, CoreMethods.PhpValue.get_Long)
                            .Expect(SpecialType.System_Int64);
                    }
                    else if (IsBooleanOnly(tmask))
                    {
                        EmitPhpValueAddr();
                        return EmitCall(ILOpCode.Call, CoreMethods.PhpValue.get_Boolean)
                            .Expect(SpecialType.System_Boolean);
                    }
                    else if (IsReadonlyStringOnly(tmask))
                    {
                        EmitPhpValueAddr();
                        return EmitCall(ILOpCode.Call, CoreMethods.PhpValue.get_String)
                            .Expect(SpecialType.System_String);
                    }
                }
                else if (stack.IsReferenceType && this.Routine != null)
                {
                    var tref = this.Routine.TypeRefContext.GetTypes(tmask)[0];
                    if (tref.IsObject)
                    {
                        HashSet<DiagnosticInfo> useSiteDiagnostic = null;
                        var t = _routine.DeclaringCompilation.SourceAssembly.GetTypeByMetadataName(tref.QualifiedName.ClrName(), true, false);
                        if (t != null && !t.IsErrorType() && t.IsDerivedFrom(stack, false, ref useSiteDiagnostic)) // TODO: or interface
                        {
                            EmitCastClass(t);
                            return t;
                        }
                        else
                        {
                            // TODO: class aliasing
                            //Debug.Assert(t != null);
                            if (t == null)
                            {
                                Debug.WriteLine($"'tref.QualifiedName' is unknown!");
                            }
                        }
                    }
                }
            }

            //
            return stack;
        }

        /// <summary>
        /// In case there is <c>Int32</c> or <c>bool</c> on the top of evaluation stack,
        /// converts it to <c>Int64</c>.
        /// </summary>
        /// <param name="stack">New type on top of stack.</param>
        /// <returns></returns>
        internal TypeSymbol EmitConvertIntToLong(TypeSymbol stack)
        {
            if (stack.SpecialType == SpecialType.System_Int32 ||
                stack.SpecialType == SpecialType.System_Boolean)
            {
                _il.EmitOpCode(ILOpCode.Conv_i8);    // int|bool -> long
                stack = this.CoreTypes.Long;
            }

            return stack;
        }

        /// <summary>
        /// In case there is <c>Int32</c> or <c>bool</c> or <c>PhpNumber</c> on the top of evaluation stack,
        /// converts it to <c>double</c>.
        /// </summary>
        internal TypeSymbol EmitConvertNumberToDouble(BoundExpression expr)
        {
            // emit number literal directly as double
            var constant = expr.ConstantValue;
            if (constant.HasValue)
            {
                if (constant.Value is long)
                {
                    _il.EmitDoubleConstant((long)constant.Value);
                    return this.CoreTypes.Double;
                }
                if (constant.Value is int)
                {
                    _il.EmitDoubleConstant((int)constant.Value);
                    return this.CoreTypes.Double;
                }
                if (constant.Value is bool)
                {
                    _il.EmitDoubleConstant((bool)constant.Value ? 1.0 : 0.0);
                    return this.CoreTypes.Double;
                }
            }

            // emit fast ToDouble() in case of a PhpNumber variable
            var place = PlaceOrNull(expr);
            var type = TryEmitVariableSpecialize(place, expr.TypeRefMask);
            if (type == null)
            {
                if (place != null && place.HasAddress)
                {
                    if (place.TypeOpt == CoreTypes.PhpNumber)
                    {
                        place.EmitLoadAddress(_il);
                        return EmitCall(ILOpCode.Call, CoreMethods.PhpNumber.ToDouble)
                            .Expect(SpecialType.System_Double);
                    }
                }

                type = EmitSpecialize(expr);
            }

            Debug.Assert(type != null);

            if (type.SpecialType == SpecialType.System_Int32 ||
                type.SpecialType == SpecialType.System_Int64 ||
                type.SpecialType == SpecialType.System_Boolean)
            {
                _il.EmitOpCode(ILOpCode.Conv_r8);    // int|bool -> long
                type = this.CoreTypes.Double;
            }
            else if (type == CoreTypes.PhpNumber)
            {
                EmitPhpNumberAddr();
                EmitCall(ILOpCode.Call, CoreMethods.PhpNumber.ToDouble);    // number -> double
                type = this.CoreTypes.Double;
            }

            //
            return type;
        }

        public void EmitOpCode(ILOpCode code) => _il.EmitOpCode(code);

        public void EmitPop(TypeSymbol type)
        {
            Contract.ThrowIfNull(type);

            if (type.SpecialType != SpecialType.System_Void)
            {
                _il.EmitOpCode(ILOpCode.Pop, -1);
            }
        }

        public TypeSymbol Emit(BoundExpression expr)
        {
            Contract.ThrowIfNull(expr);
            return EmitSpecialize(expr);
        }

        /// <summary>
        /// Loads <see cref="RuntimeTypeHandle"/> of given type.
        /// </summary>
        public TypeSymbol EmitLoadToken(TypeSymbol type, SyntaxNode syntaxNodeOpt)
        {
            if (type != null)
            {
                _il.EmitLoadToken(_moduleBuilder, _diagnostics, type, syntaxNodeOpt);
            }
            else
            {
                EmitLoadDefaultOfValueType(this.CoreTypes.RuntimeTypeHandle);
            }

            return this.CoreTypes.RuntimeTypeHandle;
        }

        /// <summary>
        /// Loads <see cref="RuntimeMethodHandle"/> of given method.
        /// </summary>
        public TypeSymbol EmitLoadToken(MethodSymbol method, SyntaxNode syntaxNodeOpt)
        {
            _il.EmitLoadToken(_moduleBuilder, _diagnostics, method, syntaxNodeOpt);

            return this.CoreTypes.RuntimeMethodHandle;
        }

        public void EmitBox(TypeSymbol valuetype)
        {
            Contract.ThrowIfNull(valuetype);

            if (valuetype.IsValueType)
            {
                _il.EmitOpCode(ILOpCode.Box);
                EmitSymbolToken(valuetype, null);
            }
        }

        /// <summary>
        /// Emits "!= 0" operation. This method expects I4 value on top of evaluation stack.
        /// </summary>
        public void EmitLogicNegation()
        {
            _il.EmitOpCode(ILOpCode.Ldc_i4_0, 1);
            _il.EmitOpCode(ILOpCode.Ceq);
        }

        /// <summary>
        /// Loads field address on top of evaluation stack.
        /// </summary>
        public void EmitFieldAddress(FieldSymbol fld)
        {
            Debug.Assert(fld != null);
            _il.EmitOpCode(fld.IsStatic ? ILOpCode.Ldsflda : ILOpCode.Ldflda);
            EmitSymbolToken(fld, null);
        }

        internal void EmitSymbolToken(TypeSymbol symbol, SyntaxNode syntaxNode)
        {
            _il.EmitSymbolToken(_moduleBuilder, _diagnostics, symbol, syntaxNode);
        }

        internal void EmitSymbolToken(FieldSymbol symbol, SyntaxNode syntaxNode)
        {
            _il.EmitSymbolToken(_moduleBuilder, _diagnostics, symbol, syntaxNode);
        }

        //private void EmitSymbolToken(MethodSymbol method, SyntaxNode syntaxNode)
        //{
        //    _il.EmitToken(_moduleBuilder.Translate(method, syntaxNode, _diagnostics, null), syntaxNode, _diagnostics);
        //}

        internal void EmitSequencePoint(LangElement element)
        {
            if (element != null)
            {
                EmitSequencePoint(element.Span);
            }
        }
        internal void EmitSequencePoint(Span span)
        {
            if (_emitPdbSequencePoints && span.IsValid)
            {
                if (_lazySyntaxTree == null)
                    _lazySyntaxTree = new SyntaxTreeAdapter(_routine.ContainingFile.Syntax.ContainingSourceUnit);

                _il.DefineSequencePoint(_lazySyntaxTree, new Microsoft.CodeAnalysis.Text.TextSpan(span.Start, span.Length));
            }
        }
        SyntaxTree _lazySyntaxTree;

        /// <summary>
        /// Emits load of <c>PhpAlias.Value</c>,
        /// expecting <c>PhpAlias</c> on top of evaluation stack,
        /// pushing <c>PhpValue</c> on top of the stack.
        /// </summary>
        public TypeSymbol Emit_PhpAlias_GetValue()
        {
            // <stack>.Value
            EmitOpCode(ILOpCode.Ldfld);
            EmitSymbolToken(CoreMethods.PhpAlias.Value, null);
            return this.CoreTypes.PhpValue;
        }

        /// <summary>
        /// Emits load of <c>PhpAlias.Value</c>,
        /// expecting <c>PhpAlias</c> on top of evaluation stack,
        /// pushing <c>PhpValue</c> on top of the stack.
        /// </summary>
        public void Emit_PhpAlias_GetValueRef()
        {
            // ref <stack>.Value
            EmitOpCode(ILOpCode.Ldflda);
            EmitSymbolToken(CoreMethods.PhpAlias.Value, null);
        }

        /// <summary>
        /// Emits store to <c>PhpAlias.Value</c>,
        /// expecting <c>PhpAlias</c> and <c>PhpValue</c> on top of evaluation stack.
        /// </summary>
        public void Emit_PhpAlias_SetValue()
        {
            // <stack_1>.Value = <stack_2>
            EmitOpCode(ILOpCode.Stfld);
            EmitSymbolToken(CoreMethods.PhpAlias.Value, null);
        }

        /// <summary>
        /// Emits <c>new PhpAlias</c>, expecting <c>PhpValue</c> on top of the evaluation stack.
        /// </summary>
        public TypeSymbol Emit_PhpValue_MakeAlias()
        {
            // new PhpAlias(<STACK>, 1)
            _il.EmitIntConstant(1);
            return EmitCall(ILOpCode.Newobj, CoreMethods.Ctors.PhpAlias_PhpValue_int);
        }

        /// <summary>
        /// Emits load of PhpValue representing void.
        /// </summary>
        public TypeSymbol Emit_PhpValue_Void()
            => Emit_PhpValue_Void(_il, _moduleBuilder, _diagnostics);

        /// <summary>
        /// Emits load of PhpValue representing void.
        /// </summary>
        static TypeSymbol Emit_PhpValue_Void(ILBuilder il, Emit.PEModuleBuilder module, DiagnosticBag diagnostic)
        {
            il.EmitOpCode(ILOpCode.Ldsfld);
            il.EmitSymbolToken(module, diagnostic, module.Compilation.CoreMethods.PhpValue.Void, null);
            return module.Compilation.CoreTypes.PhpValue;
        }

        /// <summary>
        /// Emits load of PhpValue representing null.
        /// </summary>
        public TypeSymbol Emit_PhpValue_Null()
        {
            _il.EmitOpCode(ILOpCode.Ldsfld);
            EmitSymbolToken(CoreMethods.PhpValue.Null, null);
            return CoreTypes.PhpValue;
        }

        public void Emit_NewArray(TypeSymbol elementType, BoundExpression[] values)
        {
            _il.EmitIntConstant(values.Length);
            _il.EmitOpCode(ILOpCode.Newarr);
            EmitSymbolToken(elementType, null);

            if (values.Length != 0)
            {
                // new []{ argI, ..., argN }
                for (int i = 0; i < values.Length; i++)
                {
                    _il.EmitOpCode(ILOpCode.Dup);   // <array>
                    _il.EmitIntConstant(i);         // [i]
                    EmitConvert(values[i], elementType);
                    _il.EmitOpCode(ILOpCode.Stelem);
                    EmitSymbolToken(elementType, null);
                }
            }
            else
            {
                // empty array
                // TODO: use static singleton
            }
        }

        public void EmitUnset(BoundReferenceExpression expr)
        {
            Debug.Assert(expr != null);

            if (!expr.Access.IsUnset)
                throw new ArgumentException();

            var place = expr.BindPlace(this);
            Debug.Assert(place != null);

            place.EmitStorePrepare(this);
            place.EmitStore(this, null);
        }

        /// <summary>
        /// Emits call to given method.
        /// </summary>
        /// <param name="code">Call op code, Call, Callvirt, Calli.</param>
        /// <param name="method">Method reference.</param>
        /// <returns>Method return type.</returns>
        internal TypeSymbol EmitCall(ILOpCode code, MethodSymbol method)
        {
            return _il.EmitCall(_moduleBuilder, _diagnostics, code, method);
        }

        internal TypeSymbol EmitCall(ILOpCode code, MethodSymbol method, BoundExpression thisExpr, ImmutableArray<BoundExpression> arguments)
        {
            Contract.ThrowIfNull(method);

            TypeSymbol thisType;

            // <this>
            if (thisExpr != null)
            {
                thisType = Emit(thisExpr);
                Debug.Assert(thisType != null && thisType.SpecialType != SpecialType.System_Void);

                if (method.HasThis)
                {
                    if (thisType.IsValueType)
                    {
                        EmitStructAddr(thisType);   // value -> valueref
                    }
                }
                else
                {
                    EmitPop(thisType);
                    thisType = null;
                }
            }
            else
            {
                if (method.HasThis && code != ILOpCode.Newobj)
                {
                    if (ThisPlaceOpt != null && ThisPlaceOpt.TypeOpt != null &&
                        ThisPlaceOpt.TypeOpt.IsEqualToOrDerivedFrom(method.ContainingType))
                    {
                        // implicit $this instance
                        thisType = EmitThis();
                        code = ILOpCode.Call;   // instead of .callvirt
                    }
                    else
                    {
                        throw new ArgumentException();  // TODO: PHP would create temporary instance of class
                    }
                }

                thisType = null;
            }

            // arguments
            var parameters = method.Parameters;
            int arg_index = 0;      // next argument to be emitted from <arguments>
            var param_index = 0;    // loaded parameters

            for (; param_index < parameters.Length; param_index++)
            {
                var p = parameters[param_index];

                // special implicit parameters
                if (arg_index == 0 && p.IsImplicitlyDeclared)
                {
                    // <ctx>
                    if (SpecialParameterSymbol.IsContextParameter(p))
                    {
                        EmitLoadContext();
                        continue;
                    }
                    // TypeCtx, Locals, ...
                    throw new NotImplementedException();
                }

                // load arguments
                if (p.IsParams)
                {
                    Debug.Assert(p.Type.IsArray());

                    // wrap remaining arguments to array
                    var values = (arg_index < arguments.Length) ? arguments.Skip(arg_index).ToArray() : new BoundExpression[0];
                    arg_index += values.Length;
                    Emit_NewArray(((ArrayTypeSymbol)p.Type).ElementType, values);
                    break;  // p is last one
                }

                if (arg_index < arguments.Length)
                {
                    EmitConvert(arguments[arg_index++], p.Type); // load argument
                }
                else
                {
                    EmitParameterDefaultValue(p);
                }
            }

            // emit remaining not used arguments
            for (; arg_index < arguments.Length; arg_index++)
            {
                EmitPop(Emit(arguments[arg_index]));
            }

            // call the method
            return EmitCall(code, method);
        }

        /// <summary>
        /// Emits .call to <paramref name="target"/> assuming it takes the same arguments as passed to the caller method (<paramref name="thismethod"/>).
        /// </summary>
        /// <param name="target">Method to be called.</param>
        /// <param name="thismethod">Current method.</param>
        /// <returns>Return of <paramref name="target"/>.</returns>
        internal TypeSymbol EmitThisCall(MethodSymbol target, MethodSymbol thismethod)
        {
            if (target == null)
            {
                return CoreTypes.Void;
            }

            if (target.HasThis)
            {
                Debug.Assert(thismethod.HasThis);
                Debug.Assert(this.ThisPlaceOpt != null);
                Debug.Assert(this.ThisPlaceOpt.TypeOpt.IsEqualToOrDerivedFrom(target.ContainingType));
                this.EmitThis();
            }

            var targetps = target.Parameters;
            var givenps = thismethod.Parameters;

            int srcp = 0;
            while (srcp < givenps.Length && givenps[srcp].IsImplicitlyDeclared)
            {
                srcp++;
            }

            for (int i = 0; i < targetps.Length; i++)
            {
                var targetp = targetps[i];
                if (targetp.IsImplicitlyDeclared)
                {
                    if (SpecialParameterSymbol.IsContextParameter(targetp))
                    {
                        this.EmitLoadContext();
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                }
                else
                {
                    if (srcp < givenps.Length)
                    {
                        var p = givenps[srcp];
                        var ptype = new ParamPlace(p).EmitLoad(Builder);
                        EmitConvert(ptype, 0, targetp.Type);
                    }
                    else
                    {
                        EmitParameterDefaultValue(targetp);
                    }

                    srcp++;
                }
            }

            //
            return this.EmitCall(ILOpCode.Call, target);
        }

        /// <summary>
        /// Initializes place with a default value.
        /// This applies to structs without default ctor that won't work properly when uninitialized.
        /// </summary>
        internal void EmitInitializePlace(IPlace place)
        {
            Contract.ThrowIfNull(place);
            var t = place.TypeOpt;
            Contract.ThrowIfNull(t);

            switch (t.SpecialType)
            {
                // we don't have to initialize those:
                case SpecialType.System_Boolean:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_Double:
                case SpecialType.System_Object:
                    break;

                default:
                    if (t.IsValueType || t == CoreTypes.PhpAlias)   // PhpValue, PhpNumber, PhpAlias
                    {
                        // fld = default(T)
                        place.EmitStorePrepare(_il);
                        EmitLoadDefault(t, 0);
                        place.EmitStore(_il);
                    }
                    break;
            }
        }

        /// <summary>
        /// Emits default value of given parameter.
        /// Puts value of target parameter's type.
        /// </summary>
        /// <param name="targetp">Parameter to emit its default value.</param>
        void EmitParameterDefaultValue(ParameterSymbol targetp)
        {
            Contract.ThrowIfNull(targetp);

            //
            TypeSymbol ptype; // emitted type to be eventually converted to target parameter type

            // emit targetp default value:
            ConstantValue cvalue;

            // TODO: targetp.IsParams

            var boundinitializer = (targetp as SourceParameterSymbol)?.Initializer;
            if (boundinitializer != null)
            {
                EmitConvert(boundinitializer, ptype = targetp.Type);
            }
            else if ((cvalue = targetp.ExplicitDefaultConstantValue) != null)
            {
                ptype = EmitLoadConstant(cvalue.Value, targetp.Type);
            }
            else
            {
                ptype = targetp.Type;
                EmitLoadDefault(ptype, 0);
            }

            // eventually convert emitted value to target parameter type
            EmitConvert(ptype, 0, targetp.Type);
        }

        internal TypeSymbol EmitGetProperty(IPlace holder, PropertySymbol prop)
        {
            Debug.Assert(prop.IsStatic || holder != null);
            Debug.Assert(prop.GetMethod != null);
            Debug.Assert(prop.GetMethod.ParameterCount == 0);

            var getter = prop.GetMethod;

            if (holder != null && !getter.IsStatic)
            {
                Debug.Assert(holder.TypeOpt != null);
                if (holder.TypeOpt.IsValueType)
                {
                    holder.EmitLoadAddress(_il);
                }
                else
                {
                    holder.EmitLoad(_il);
                }
            }

            return EmitCall(getter.IsVirtual ? ILOpCode.Callvirt : ILOpCode.Call, getter);
        }

        internal void EmitCastClass(TypeSymbol from, TypeSymbol to)
        {
            if (!from.IsEqualToOrDerivedFrom(to))
            {
                EmitCastClass(to);
            }
        }

        internal void EmitCastClass(TypeSymbol type)
        {
            // (T)
            _il.EmitOpCode(ILOpCode.Castclass);
            EmitSymbolToken(type, null);
        }

        public void EmitEcho(BoundExpression expr)
        {
            Contract.ThrowIfNull(expr);

            // Template: <ctx>.Echo(expr);

            this.EmitLoadContext();
            var type = EmitSpecialize(expr);

            //
            MethodSymbol method = null;

            switch (type.SpecialType)
            {
                case SpecialType.System_Void:
                    EmitPop(this.CoreTypes.Context);
                    return;
                case SpecialType.System_String:
                    method = CoreMethods.Operators.Echo_String.Symbol;
                    break;
                case SpecialType.System_Double:
                    method = CoreMethods.Operators.Echo_Double.Symbol;
                    break;
                case SpecialType.System_Int32:
                    method = CoreMethods.Operators.Echo_Int32.Symbol;
                    break;
                case SpecialType.System_Int64:
                    method = CoreMethods.Operators.Echo_Long.Symbol;
                    break;
                case SpecialType.System_Boolean:
                    // TODO: if (VALUE) echo("1");
                    EmitCall(ILOpCode.Call, CoreMethods.Operators.ToString_Bool).Expect(SpecialType.System_String);
                    method = CoreMethods.Operators.Echo_String.Symbol;
                    break;
                default:
                    if (type == CoreTypes.PhpString)
                    {
                        method = CoreMethods.Operators.Echo_PhpString.Symbol;
                    }
                    else if (type == CoreTypes.PhpNumber)
                    {
                        method = CoreMethods.Operators.Echo_PhpNumber.Symbol;
                    }
                    else if (type == CoreTypes.PhpValue)
                    {
                        method = CoreMethods.Operators.Echo_PhpValue.Symbol;
                    }
                    else if (type == CoreTypes.PhpAlias)
                    {
                        Emit_PhpAlias_GetValue();
                        method = CoreMethods.Operators.Echo_PhpValue.Symbol;
                    }
                    else if (type.IsOfType(CoreTypes.IPhpArray))
                    {
                        this.EmitConvertToString(type, 0);  // Array -> string
                        method = CoreMethods.Operators.Echo_String.Symbol;
                    }
                    else
                    {
                        // TODO: check expr.TypeRefMask if it is only NULL
                        EmitBox(type);
                        method = CoreMethods.Operators.Echo_Object.Symbol;
                    }
                    break;
            }

            //
            Debug.Assert(method != null);
            EmitCall(ILOpCode.Call, method);
        }

        /// <summary>
        /// Emits the expression decorated with error reporting disabling routine.
        /// </summary>
        public TypeSymbol EmitWithDisabledErrorReporting(BoundExpression expr)
        {
            //		context.DisableErrorReporting();
            //		<expr>
            //		context.EnableErrorReporting();

            EmitLoadContext();
            EmitCall(ILOpCode.Callvirt, CoreMethods.Context.DisableErrorReporting)
                .Expect(SpecialType.System_Void);

            var t = Emit(expr);

            EmitLoadContext();
            EmitCall(ILOpCode.Callvirt, CoreMethods.Context.EnableErrorReporting)
                .Expect(SpecialType.System_Void);

            //
            return t;
        }

        public void EmitIntStringKey(int key)
        {
            _il.EmitIntConstant(key);
            EmitCall(ILOpCode.Newobj, CoreMethods.Ctors.IntStringKey_int);
        }

        public void EmitIntStringKey(BoundExpression expr)
        {
            Contract.ThrowIfNull(expr);

            var constant = expr.ConstantValue;
            if (constant.HasValue)
            {
                if (constant.Value is string)
                {
                    _il.EmitStringConstant((string)constant.Value);
                    EmitCall(ILOpCode.Newobj, CoreMethods.Ctors.IntStringKey_string);
                }
                else if (constant.Value is long)
                {
                    EmitIntStringKey((int)(long)constant.Value);
                }
                else if (constant.Value is int)
                {
                    EmitIntStringKey((int)constant.Value);
                }
                else
                {
                    throw new NotSupportedException();
                }

                return;
            }

            var t = Emit(expr); // TODO: ConvertToArrayKey
            switch (t.SpecialType)
            {
                case SpecialType.System_Int64:
                    _il.EmitOpCode(ILOpCode.Conv_i4);   // i8 -> i4
                    goto case SpecialType.System_Int32;
                case SpecialType.System_Int32:
                    EmitCall(ILOpCode.Newobj, CoreMethods.Ctors.IntStringKey_int);
                    break;
                case SpecialType.System_String:
                    EmitCall(ILOpCode.Newobj, CoreMethods.Ctors.IntStringKey_string);
                    break;
                default:
                    EmitConvertToPhpValue(t, 0);
                    EmitCall(ILOpCode.Call, CoreMethods.Operators.ToIntStringKey_PhpValue);
                    break;
            }
        }

        /// <summary>
        /// Emits declaring function into the context.
        /// </summary>
        public void EmitDeclareFunction(SourceFunctionSymbol f)
        {
            Debug.Assert(f != null);

            var field = f.EnsureRoutineInfoField(_moduleBuilder);

            // <ctx>.DeclareFunction(RoutineInfo)
            EmitLoadContext();
            new FieldPlace(null, field).EmitLoad(_il);

            EmitCall(ILOpCode.Call, CoreMethods.Context.DeclareFunction_RoutineInfo);
        }

        /// <summary>
        /// Emits declaring type into the context.
        /// </summary>
        public void EmitDeclareType(SourceTypeSymbol t)
        {
            Debug.Assert(t != null);

            // <ctx>.DeclareType<T>()
            EmitLoadContext();
            EmitCall(ILOpCode.Call, CoreMethods.Context.DeclareType_T.Symbol.Construct(t));
        }

        /// <summary>
        /// Emits call to main method.
        /// </summary>
        /// <param name="mainmethod">Static Main method representing the script global code.</param>
        /// <returns>Main method result value type.</returns>
        public TypeSymbol EmitCallMain(MethodSymbol mainmethod)
        {
            Contract.ThrowIfNull(mainmethod);
            Debug.Assert(mainmethod.IsStatic);
            Debug.Assert(mainmethod.Name == WellKnownPchpNames.GlobalRoutineName);

            foreach (var p in mainmethod.Parameters)
            {
                switch (p.Name)
                {
                    case SpecialParameterSymbol.ContextName:
                        EmitLoadContext();
                        break;
                    case SpecialParameterSymbol.LocalsName:
                        Debug.Assert(LocalsPlaceOpt != null);
                        LocalsPlaceOpt.EmitLoad(_il);
                        break;
                    default:
                        if (p.Name == SpecialParameterSymbol.ThisName)
                        {
                            EmitThisOrNull();
                            break;
                        }
                        throw ExceptionUtilities.UnexpectedValue(p.Name);
                }
            }

            //
            return EmitCall(ILOpCode.Call, mainmethod);
        }

        public TypeSymbol EmitLoadConstant(object value, TypeSymbol targetOpt = null)
        {
            if (value == null)
            {
                if (targetOpt != null)
                {
                    EmitLoadDefault(targetOpt, 0);
                    return targetOpt;
                }
                else
                {
                    Builder.EmitNullConstant();
                    return CoreTypes.Object;
                }
            }
            else
            {
                if (value is int)
                {
                    if (targetOpt != null)
                    {
                        switch (targetOpt.SpecialType)
                        {
                            case SpecialType.System_Boolean:
                                _il.EmitBoolConstant((int)value != 0);
                                return targetOpt;
                            case SpecialType.System_String:
                                _il.EmitStringConstant(value.ToString());
                                return targetOpt;
                        }
                    }

                    Builder.EmitIntConstant((int)value);
                    return CoreTypes.Int32;
                }
                else if (value is long)
                {
                    if (targetOpt != null)
                    {
                        switch (targetOpt.SpecialType)
                        {
                            case SpecialType.System_Boolean:
                                _il.EmitBoolConstant((long)value != 0);
                                return targetOpt;
                            case SpecialType.System_String:
                                _il.EmitStringConstant(value.ToString());
                                return targetOpt;
                        }
                    }

                    Builder.EmitLongConstant((long)value);
                    return CoreTypes.Long;
                }
                else if (value is string)
                {
                    Builder.EmitStringConstant((string)value);
                    return CoreTypes.String;
                }
                else if (value is bool)
                {
                    if (targetOpt != null)
                    {
                        switch (targetOpt.SpecialType)
                        {
                            case SpecialType.System_String:
                                _il.EmitStringConstant((bool)value ? "1" : "");
                                return targetOpt;
                        }
                    }

                    Builder.EmitBoolConstant((bool)value);
                    return CoreTypes.Boolean;
                }
                else if (value is double)
                {
                    if (targetOpt != null)
                    {
                        switch (targetOpt.SpecialType)
                        {
                            case SpecialType.System_Boolean:
                                _il.EmitBoolConstant((double)value != 0.0);
                                return targetOpt;
                            case SpecialType.System_Int64:
                                _il.EmitLongConstant((long)(double)value);
                                return targetOpt;
                        }
                    }

                    Builder.EmitDoubleConstant((double)value);
                    return CoreTypes.Double;
                }
                else
                {
                    throw ExceptionUtilities.UnexpectedValue(value);
                }
            }
        }

        public void EmitLoadDefault(TypeSymbol type, TypeRefMask typemask)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Void:
                    break;
                case SpecialType.System_Double:
                    _il.EmitDoubleConstant(0.0);
                    break;
                case SpecialType.System_Int32:
                    _il.EmitIntConstant(0);
                    break;
                case SpecialType.System_Int64:
                    _il.EmitLongConstant(0);
                    break;
                case SpecialType.System_Boolean:
                    _il.EmitBoolConstant(false);
                    break;
                case SpecialType.System_String:
                    _il.EmitStringConstant(string.Empty);
                    break;
                default:
                    if (type.IsReferenceType)
                    {
                        if (type == CoreTypes.PhpArray || type == CoreTypes.IPhpArray)
                        {
                            EmitCall(ILOpCode.Newobj, CoreMethods.Ctors.PhpArray);
                        }
                        else if (type == CoreTypes.PhpAlias)
                        {
                            // new PhpAlias(void, 1);
                            Emit_PhpValue_Void();
                            Emit_PhpValue_MakeAlias();
                        }
                        else if (type == CoreTypes.PhpString)
                        {
                            EmitCall(ILOpCode.Newobj, CoreMethods.Ctors.PhpString);
                        }
                        else
                        {
                            _il.EmitNullConstant();
                        }
                    }
                    else
                    {
                        if (type == CoreTypes.PhpValue)
                        {
                            if (typemask.IsSingleType && this.Routine != null)
                            {
                                var typectx = this.Routine.TypeRefContext;

                                if (typectx.IsBoolean(typemask))
                                {
                                    _il.EmitBoolConstant(false);
                                    EmitCall(ILOpCode.Call, CoreMethods.PhpValue.Create_Boolean);
                                    break;
                                }
                                else if (typectx.IsLong(typemask))
                                {
                                    _il.EmitLongConstant(0);
                                    EmitCall(ILOpCode.Call, CoreMethods.PhpValue.Create_Long);
                                    break;
                                }
                                else if (typectx.IsDouble(typemask))
                                {
                                    _il.EmitDoubleConstant(0.0);
                                    EmitCall(ILOpCode.Call, CoreMethods.PhpValue.Create_Double);
                                    break;
                                }
                                else if (typectx.IsAString(typemask))
                                {
                                    // return ""
                                    _il.EmitStringConstant(string.Empty);
                                    EmitCall(ILOpCode.Call, CoreMethods.PhpValue.Create_String);
                                    break;
                                }
                                else if (typectx.IsArray(typemask))
                                {
                                    EmitCall(ILOpCode.Newobj, CoreMethods.Ctors.PhpArray);
                                    EmitCall(ILOpCode.Call, CoreMethods.PhpValue.Create_PhpArray);
                                    break;
                                }
                            }
                        }

                        //
                        EmitLoadDefaultOfValueType(type);
                    }
                    break;
            }
        }

        /// <summary>
        /// Emits <c>default(valuetype)</c>.
        /// Handles special types with a default ctor.
        /// </summary>
        public void EmitLoadDefaultOfValueType(TypeSymbol valuetype)
        {
            Debug.Assert(valuetype != null && valuetype.IsValueType);

            if (valuetype == CoreTypes.PhpNumber)
            {
                // PhpNumber.Default ~ 0L
                _il.EmitOpCode(ILOpCode.Ldsfld);
                EmitSymbolToken(CoreMethods.PhpNumber.Default, null);
            }
            else if (valuetype == CoreTypes.PhpValue)
            {
                // PhpValue.Void
                Emit_PhpValue_Void();
            }
            else
            {
                // default(T)

                var loc = this.GetTemporaryLocal(valuetype, true);

                // ldloca <loc>
                // .initobj <type>
                Builder.EmitLocalAddress(loc);
                Builder.EmitOpCode(ILOpCode.Initobj);
                EmitSymbolToken(valuetype, null);
                // ldloc <loc>
                Builder.EmitLocalLoad(loc);
            }
        }

        public void EmitRetDefault()
        {
            // return default(RETURN_TYPE);

            var return_type = this.Routine.ReturnType;

            EmitLoadDefault(return_type, this.Routine.ResultTypeMask);
            EmitRet(return_type);
        }

        /// <summary>
        /// Emits .ret instruction with sequence point at closing brace.
        /// </summary>
        public void EmitRet(TypeSymbol stack)
        {
            // sequence point
            var body = AstUtils.BodySpanOrInvalid(Routine?.Syntax);
            if (body.IsValid)
            {
                EmitSequencePoint(new Span(body.End - 1, 1));
                EmitOpCode(ILOpCode.Nop);
            }

            //
            if (_il.InExceptionHandler)
            {
                ((ExitBlock)this.Routine.ControlFlowGraph.Exit).EmitTmpRet(this, stack);
            }
            else
            {
                _il.EmitRet(stack.SpecialType == SpecialType.System_Void);
            }
        }

        /// <summary>
        /// Emits <c>place != null</c> expression.
        /// </summary>
        public void EmitNotNull(IPlace place)
        {
            Debug.Assert(place != null);
            Debug.Assert(place.TypeOpt.IsReferenceType);

            // {place} != null : boolean
            place.EmitLoad(_il);
            _il.EmitNullConstant();
            _il.EmitOpCode(ILOpCode.Cgt_un);
        }

        /// <summary>
        /// Emits <c>Debug.Assert([<paramref name="place"/>]) in debug compile mode.</c>
        /// </summary>
        /// <param name="place">The variable to emit assertion for.</param>
        /// <param name="messageOpt">Optional second argument for assert.</param>
        public void EmitDebugAssertNotNull(IPlace place, string messageOpt = null)
        {
            if (IsDebug)
            {
                //EmitNotNull(place);
                //EmitDebugAssert(messageOpt);
            }
        }

        /// <summary>
        /// Emits <c>Debug.Assert([stack]).</c>
        /// </summary>
        /// <param name="messageOpt">Optional second argument for assert.</param>
        public void EmitDebugAssert(string messageOpt = null)
        {
            //var dt = this.DeclaringCompilation.GetTypeByMetadataName("System.Diagnostics.Debug"); // System.dll
            //dt.GetMember("Assert")
            throw new NotImplementedException();
        }
    }

    internal static class ILBuilderExtension
    {
        public static void EmitLoadToken(this ILBuilder il, PEModuleBuilder module, DiagnosticBag diagnostics, TypeSymbol type, SyntaxNode syntaxNodeOpt)
        {
            il.EmitOpCode(ILOpCode.Ldtoken);
            EmitSymbolToken(il, module, diagnostics, type, syntaxNodeOpt);
        }

        public static void EmitLoadToken(this ILBuilder il, PEModuleBuilder module, DiagnosticBag diagnostics, MethodSymbol method, SyntaxNode syntaxNodeOpt)
        {
            il.EmitOpCode(ILOpCode.Ldtoken);
            EmitSymbolToken(il, module, diagnostics, method, syntaxNodeOpt);
        }

        public static void EmitSymbolToken(this ILBuilder il, PEModuleBuilder module, DiagnosticBag diagnostics, TypeSymbol symbol, SyntaxNode syntaxNode)
        {
            il.EmitToken(module.Translate(symbol, syntaxNode, diagnostics), syntaxNode, diagnostics);
        }

        public static void EmitSymbolToken(this ILBuilder il, PEModuleBuilder module, DiagnosticBag diagnostics, MethodSymbol symbol, SyntaxNode syntaxNode)
        {
            il.EmitToken(module.Translate(symbol, syntaxNode, diagnostics, true), syntaxNode, diagnostics);
        }

        public static void EmitSymbolToken(this ILBuilder il, PEModuleBuilder module, DiagnosticBag diagnostics, FieldSymbol symbol, SyntaxNode syntaxNode)
        {
            il.EmitToken(symbol, syntaxNode, diagnostics);
        }

        /// <summary>
        /// Emits call to given method.
        /// </summary>
        /// <returns>Method return type.</returns>
        public static TypeSymbol EmitCall(this ILBuilder il, PEModuleBuilder module, DiagnosticBag diagnostics, ILOpCode code, MethodSymbol method)
        {
            Contract.ThrowIfNull(method);
            Debug.Assert(code == ILOpCode.Call || code == ILOpCode.Calli || code == ILOpCode.Callvirt || code == ILOpCode.Newobj);

            var stack = method.GetCallStackBehavior();

            if (code == ILOpCode.Newobj)
                stack += 1 + 1;    // there is no <this>, + it pushes <newinst> on stack

            il.EmitOpCode(code, stack);
            il.EmitToken(module.Translate(method, diagnostics, false), null, diagnostics);
            return (code == ILOpCode.Newobj) ? method.ContainingType : method.ReturnType;
        }
    }
}
