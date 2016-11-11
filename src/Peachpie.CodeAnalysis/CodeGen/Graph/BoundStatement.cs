﻿using Microsoft.CodeAnalysis;
using Pchp.CodeAnalysis.CodeGen;
using Pchp.CodeAnalysis.Symbols;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using Devsense.PHP.Text;

namespace Pchp.CodeAnalysis.Semantics
{
    partial class BoundStatement : IGenerator
    {
        internal virtual void Emit(CodeGenerator cg)
        {
            throw new NotImplementedException();
        }

        void IGenerator.Generate(CodeGenerator cg) => Emit(cg);
    }

    partial class BoundEmptyStatement
    {
        internal override void Emit(CodeGenerator cg)
        {
            // nop
        }
    }

    partial class BoundExpressionStatement
    {
        internal override void Emit(CodeGenerator cg)
        {
            cg.EmitSequencePoint(this.PhpSyntax);
            cg.EmitPop(this.Expression.Emit(cg));
        }
    }

    partial class BoundReturnStatement
    {
        internal override void Emit(CodeGenerator cg)
        {
            cg.EmitSequencePoint(this.PhpSyntax);

            var rtype = cg.Routine.ReturnType;
            var rvoid = rtype.SpecialType == SpecialType.System_Void;

            //
            if (this.Returned == null)
            {
                if (rvoid)
                {
                    // <void>
                }
                else
                {
                    // <default>
                    cg.EmitLoadDefault(rtype, cg.Routine.ResultTypeMask);
                }
            }
            else
            {
                if (rvoid)
                {
                    // <expr>;
                    cg.EmitPop(this.Returned.Emit(cg));
                }
                else
                {
                    // return (T)<expr>;
                    cg.EmitConvert(this.Returned, rtype);
                }
            }

            // .ret
            cg.EmitRet(rtype);
        }
    }

    partial class BoundThrowStatement
    {
        internal override void Emit(CodeGenerator cg)
        {
            cg.EmitSequencePoint(this.PhpSyntax);

            //
            var t = cg.Emit(Thrown);

            if (t.IsReferenceType)
            {
                if (!t.IsEqualToOrDerivedFrom(cg.CoreTypes.Exception))
                {
                    throw new NotImplementedException();    // Wrap to System.Exception
                }
            }
            else
            {
                //if (t == cg.CoreTypes.PhpValue)
                //{

                //}

                throw new NotImplementedException();    // Wrap to System.Exception
            }

            // throw <stack>;
            cg.Builder.EmitThrow(false);
        }
    }

    partial class BoundFunctionDeclStatement
    {
        internal override void Emit(CodeGenerator cg)
        {
            cg.EmitSequencePoint(this.FunctionDecl.HeadingSpan);

            // <ctx>.DeclareFunction ...
            cg.EmitDeclareFunction(this.Function);
        }
    }

    partial class BoundTypeDeclStatement
    {
        internal override void Emit(CodeGenerator cg)
        {
            cg.EmitSequencePoint(this.TypeDecl.HeadingSpan);

            // <ctx>.DeclareType<T>()
            cg.EmitDeclareType(this.Type);
        }
    }

    partial class BoundStaticVariableStatement
    {
        internal override void Emit(CodeGenerator cg)
        {
            cg.EmitSequencePoint(this.PhpSyntax);

            foreach (var v in _variables)
            {
                var getmethod = cg.CoreMethods.Context.GetStatic_T.Symbol.Construct(v._holder);
                var place = v._holderPlace;

                // Template: x = ctx.GetStatic<holder_x>()
                place.EmitStorePrepare(cg.Builder);

                cg.EmitLoadContext();
                cg.EmitCall(ILOpCode.Callvirt, getmethod);

                place.EmitStore(cg.Builder);

                // holder initialization routine
                EmitInit(cg.Module, cg.Diagnostics, cg.DeclaringCompilation, v._holder, (BoundExpression)v.InitialValue);

            }
        }

        void EmitInit(Emit.PEModuleBuilder module, DiagnosticBag diagnostic, PhpCompilation compilation, SynthesizedStaticLocHolder holder, BoundExpression initializer)
        {
            var loctype = holder.ValueField.Type;

            bool requiresContext = initializer != null && initializer.RequiresContext;

            if (requiresContext)
            {
                // emit Init only if it needs Context

                holder.EmitInit(module, (il) =>
                {
                    var cg = new CodeGenerator(il, module, diagnostic, OptimizationLevel.Release, false,
                        holder.ContainingType, new ArgPlace(compilation.CoreTypes.Context, 1), new ArgPlace(holder, 0));

                    var valuePlace = new FieldPlace(cg.ThisPlaceOpt, holder.ValueField);
                    
                    // Template: this.value = <initilizer>;

                    valuePlace.EmitStorePrepare(il);
                    cg.EmitConvert(initializer, valuePlace.TypeOpt);                    
                    valuePlace.EmitStore(il);

                    //
                    il.EmitRet(true);
                });
            }

            // default .ctor
            holder.EmitCtor(module, (il) =>
            {
                if (!requiresContext)
                {
                    // emit default value only if it won't be initialized by Init above

                    var cg = new CodeGenerator(il, module, diagnostic, OptimizationLevel.Release, false,
                        holder.ContainingType, null, new ArgPlace(holder, 0));

                    var valuePlace = new FieldPlace(cg.ThisPlaceOpt, holder.ValueField);

                    // Template: this.value = default(T);

                    valuePlace.EmitStorePrepare(il);
                    if (initializer != null)
                    {
                        cg.EmitConvert(initializer, valuePlace.TypeOpt);
                    }
                    else
                    {
                        cg.EmitLoadDefault(valuePlace.TypeOpt, 0);
                    }
                    valuePlace.EmitStore(il);
                }

                //
                il.EmitRet(true);
            });
        }
    }

    partial class BoundUnset
    {
        internal override void Emit(CodeGenerator cg)
        {
            cg.EmitSequencePoint(this.PhpSyntax);

            this.VarReferences.ForEach(cg.EmitUnset);
        }
    }
}
