﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
using Pchp.CodeAnalysis.Semantics.TypeRef;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.Utilities;

namespace Pchp.CodeAnalysis.FlowAnalysis.Souffle
{
    internal partial class SouffleEncoder : GraphExplorer<VoidStruct>
    {
        private readonly SourceRoutineSymbol _routine;
        private readonly Writers _writers;

        private readonly string _routineName;

        private SouffleEncoder(SourceRoutineSymbol routine, Writers writers)
        {
            _routine = routine;
            _writers = writers;

            _routineName = GetUniqueRoutineName(routine);
        }

        public static void Encode(PhpCompilation compilation, string basePath)
        {
            using var writers = new Writers(basePath);

            foreach (var routine in compilation.SourceSymbolCollection.AllRoutines)
            {
                if (routine.ControlFlowGraph == null)
                    continue;

                var encoder = new SouffleEncoder(routine, writers);
                routine.ControlFlowGraph.Accept(encoder);
            }
        }

        private static string GetUniqueRoutineName(SourceRoutineSymbol routine) =>
            $"{routine.ContainingType.GetFullName()}.{routine.MetadataName}";

        private string GetName(object obj) =>
            obj switch
            {
                BoundOperation op => $"{_routineName}:{op.GetType().Name}#{op.SerialNumber}",
                Edge edge => $"{_routineName}:{edge.GetType().Name}#{edge.SerialNumber}",
                _ => throw new NotSupportedException()
            };

        private void ExportProperty(string typeName, object parent, string propertyName, object property)
        {
            if (property != null)
            {
                _writers.WriteProperty(typeName, propertyName, GetName(parent), GetName(property));
            }
        }

        private void ExportPropertyItem(string typeName, object parent, string propertyName, int index, object property)
        {
            if (property != null)
            {
                _writers.WritePropertyItem(typeName, propertyName, GetName(parent), index, GetName(property));
            }
        }

        private void ExportPropertyEnumerable<T>(string typeName, object parent, string propertyName, IEnumerable<T> propertyItems)
        {
            string parentName = GetName(parent);

            int index = 0;
            foreach (var item in propertyItems)
            {
                _writers.WritePropertyItem(typeName, propertyName, parentName, index, GetName(item));
            }
        }

        #region Graph.Block

        private void VisitCFGBlockStatements(BoundBlock x)
        {
            for (int i = 0; i < x.Statements.Count; i++)
            {
                ExportPropertyItem(nameof(BoundBlock), x, nameof(BoundBlock.Statements), i, x.Statements[i]);
                Accept(x.Statements[i]);
            }
        }

        /// <summary>
        /// Visits block statements and its edge to next block.
        /// </summary>
        protected override void DefaultVisitUnexploredBlock(BoundBlock x)
        {
            VisitCFGBlockStatements(x);

            ExportProperty(nameof(BoundBlock), x, nameof(BoundBlock.NextEdge), x.NextEdge);
            AcceptEdge(x, x.NextEdge);
        }

        public override VoidStruct VisitCFGCatchBlock(CatchBlock x)
        {
            //ExportProperty(nameof(CatchBlock), x, nameof(CatchBlock.TypeRef), x.TypeRef);
            Accept(x.TypeRef);

            ExportProperty(nameof(CatchBlock), x, nameof(CatchBlock.Variable), x.Variable);
            Accept(x.Variable);

            DefaultVisitBlock(x);

            return default;
        }

        public override VoidStruct VisitCFGCaseBlock(CaseBlock x)
        {
            // TODO: Export
            if (!x.CaseValue.IsOnlyBoundElement) { VisitCFGBlock(x.CaseValue.PreBoundBlockFirst); }
            if (!x.CaseValue.IsEmpty) { Accept(x.CaseValue.BoundElement); }

            DefaultVisitBlock(x);

            return default;
        }

        #endregion

        #region Graph.Edge

        public override VoidStruct VisitCFGSimpleEdge(SimpleEdge x)
        {
            Debug.Assert(x.NextBlock != null && x.NextBlock == x.Target);

            ExportProperty(nameof(SimpleEdge), x, nameof(SimpleEdge.Target), x.Target);
            x.NextBlock.Accept(this);

            DefaultVisitEdge(x);

            return default;
        }

        public override VoidStruct VisitCFGConditionalEdge(ConditionalEdge x)
        {
            Accept(x.Condition);

            ExportProperty(nameof(ConditionalEdge), x, nameof(ConditionalEdge.TrueTarget), x.TrueTarget);
            x.TrueTarget.Accept(this);

            ExportProperty(nameof(ConditionalEdge), x, nameof(ConditionalEdge.FalseTarget), x.FalseTarget);
            x.FalseTarget.Accept(this);

            return default;
        }

        public override VoidStruct VisitCFGTryCatchEdge(TryCatchEdge x)
        {
            ExportProperty(nameof(TryCatchEdge), x, nameof(TryCatchEdge.BodyBlock), x.BodyBlock);
            x.BodyBlock.Accept(this);

            ExportPropertyEnumerable(nameof(Edge), x, nameof(Edge.CatchBlocks), x.CatchBlocks);
            foreach (var c in x.CatchBlocks)
                c.Accept(this);

            if (x.FinallyBlock != null)
            {
                ExportProperty(nameof(Edge), x, nameof(TryCatchEdge.FinallyBlock), x.FinallyBlock);
                x.FinallyBlock.Accept(this);
            }

            return default;
        }

        public override VoidStruct VisitCFGForeachEnumereeEdge(ForeachEnumereeEdge x)
        {
            ExportProperty(nameof(ForeachEnumereeEdge), x, nameof(ForeachEnumereeEdge.Enumeree), x.Enumeree);
            Accept(x.Enumeree);

            ExportProperty(nameof(SimpleEdge), x, nameof(SimpleEdge.Target), x.Target);
            x.NextBlock.Accept(this);

            return default;
        }

        public override VoidStruct VisitCFGForeachMoveNextEdge(ForeachMoveNextEdge x)
        {
            ExportProperty(nameof(ForeachMoveNextEdge), x, nameof(ForeachMoveNextEdge.ValueVariable), x.ValueVariable);
            Accept(x.ValueVariable);

            ExportProperty(nameof(ForeachMoveNextEdge), x, nameof(ForeachMoveNextEdge.KeyVariable), x.KeyVariable);
            Accept(x.KeyVariable);

            ExportProperty(nameof(ForeachMoveNextEdge), x, nameof(ForeachMoveNextEdge.BodyBlock), x.BodyBlock);
            x.BodyBlock.Accept(this);

            ExportProperty(nameof(Edge), x, nameof(Edge.NextBlock), x.NextBlock);
            x.NextBlock.Accept(this);

            return default;
        }

        public override VoidStruct VisitCFGSwitchEdge(SwitchEdge x)
        {
            ExportProperty(nameof(SwitchEdge), x, nameof(SwitchEdge.SwitchValue), x.SwitchValue);
            Accept(x.SwitchValue);

            //
            ExportPropertyEnumerable(nameof(Edge), x, nameof(Edge.CaseBlocks), x.CaseBlocks);
            var arr = x.CaseBlocks;
            for (int i = 0; i < arr.Length; i++)
                arr[i].Accept(this);

            return default;
        }

        #endregion

        #region Expressions

        protected override VoidStruct VisitRoutineCall(BoundRoutineCall x)
        {
            if (x.TypeArguments.IsDefaultOrEmpty == false)
            {
                for (int i = 0; i < x.TypeArguments.Length; i++)
                {
                    //ExportPropertyItem(nameof(BoundRoutineCall), x, nameof(BoundRoutineCall.TypeArguments), i, x.TypeArguments[i]);
                    x.TypeArguments[i].Accept(this);
                }
            }

            var args = x.ArgumentsInSourceOrder;
            for (int i = 0; i < args.Length; i++)
            {
                ExportPropertyItem(nameof(BoundRoutineCall), x, nameof(BoundRoutineCall.ArgumentsInSourceOrder), i, args[i]);
                VisitArgument(args[i]);
            }

            return default;
        }

        public override VoidStruct VisitLiteral(BoundLiteral x)
        {
            //VisitLiteralExpression(x);

            return default;
        }

        public override VoidStruct VisitCopyValue(BoundCopyValue x)
        {
            ExportProperty(nameof(BoundCopyValue), x, nameof(BoundCopyValue.Expression), x.Expression);
            Accept(x.Expression);

            return default;
        }

        public override VoidStruct VisitArgument(BoundArgument x)
        {
            ExportProperty(nameof(BoundArgument), x, nameof(BoundArgument.Value), x.Value);
            Accept(x.Value);

            return default;
        }

        internal override VoidStruct VisitTypeRef(BoundTypeRef x)
        {
            return base.VisitTypeRef(x);
        }

        internal override VoidStruct VisitIndirectTypeRef(BoundIndirectTypeRef x)
        {
            ExportProperty(nameof(BoundIndirectTypeRef), x, nameof(BoundIndirectTypeRef.TypeExpression), x.TypeExpression);
            Accept(x.TypeExpression);

            return base.VisitIndirectTypeRef(x);
        }

        internal override VoidStruct VisitMultipleTypeRef(BoundMultipleTypeRef x)
        {
            Debug.Assert(x != null);
            Debug.Assert(x.TypeRefs.Length > 1);

            for (int i = 0; i < x.TypeRefs.Length; i++)
            {
                ExportPropertyItem(nameof(BoundMultipleTypeRef), x, nameof(BoundMultipleTypeRef.TypeRefs), i, x.TypeRefs[i]);
                x.TypeRefs[i].Accept(this);
            }

            return default;
        }

        public override VoidStruct VisitRoutineName(BoundRoutineName x)
        {
            ExportProperty(nameof(BoundRoutineName), x, nameof(BoundRoutineName.NameExpression), x.NameExpression);
            Accept(x.NameExpression);

            return default;
        }

        public override VoidStruct VisitGlobalFunctionCall(BoundGlobalFunctionCall x)
        {
            ExportProperty(nameof(BoundGlobalFunctionCall), x, nameof(BoundGlobalFunctionCall.Name), x.Name);
            Accept(x.Name);

            VisitRoutineCall(x);

            return default;
        }

        public override VoidStruct VisitInstanceFunctionCall(BoundInstanceFunctionCall x)
        {
            ExportProperty(nameof(BoundRoutineCall), x, nameof(BoundRoutineCall.Instance), x.Instance);
            Accept(x.Instance);

            ExportProperty(nameof(BoundInstanceFunctionCall), x, nameof(BoundInstanceFunctionCall.Name), x.Name);
            Accept(x.Name);

            VisitRoutineCall(x);

            return default;
        }

        public override VoidStruct VisitStaticFunctionCall(BoundStaticFunctionCall x)
        {
            //ExportProperty(nameof(BoundStaticFunctionCall), x, nameof(BoundStaticFunctionCall.TypeRef), x.TypeRef);
            Accept(x.TypeRef);

            ExportProperty(nameof(BoundStaticFunctionCall), x, nameof(BoundStaticFunctionCall.Name), x.Name);
            Accept(x.Name);

            VisitRoutineCall(x);

            return default;
        }

        public override VoidStruct VisitEcho(BoundEcho x)
        {
            VisitRoutineCall(x);

            return default;
        }

        public override VoidStruct VisitConcat(BoundConcatEx x)
        {
            VisitRoutineCall(x);

            return default;
        }

        public override VoidStruct VisitNew(BoundNewEx x)
        {
            //ExportProperty(nameof(BoundNewEx), x, nameof(BoundNewEx.TypeRef), x.TypeRef);
            Accept(x.TypeRef);

            VisitRoutineCall(x);

            return default;
        }

        public override VoidStruct VisitInclude(BoundIncludeEx x)
        {
            VisitRoutineCall(x);

            return default;
        }

        public override VoidStruct VisitExit(BoundExitEx x)
        {
            VisitRoutineCall(x);

            return default;
        }

        public override VoidStruct VisitAssert(BoundAssertEx x)
        {
            VisitRoutineCall(x);

            return default;
        }

        public override VoidStruct VisitBinaryExpression(BoundBinaryEx x)
        {
            ExportProperty(nameof(BoundBinaryEx), x, nameof(BoundBinaryEx.Left), x.Left);
            Accept(x.Left);

            ExportProperty(nameof(BoundBinaryEx), x, nameof(BoundBinaryEx.Right), x.Right);
            Accept(x.Right);

            return default;
        }

        public override VoidStruct VisitUnaryExpression(BoundUnaryEx x)
        {
            ExportProperty(nameof(BoundUnaryEx), x, nameof(BoundUnaryEx.Operand), x.Operand);
            Accept(x.Operand);

            return default;
        }

        public override VoidStruct VisitConversion(BoundConversionEx x)
        {
            ExportProperty(nameof(BoundConversionEx), x, nameof(BoundConversionEx.Operand), x.Operand);
            Accept(x.Operand);

            ExportProperty(nameof(BoundConversionEx), x, nameof(BoundConversionEx.TargetType), x.TargetType);
            Accept(x.TargetType);

            return default;
        }

        public override VoidStruct VisitIncDec(BoundIncDecEx x)
        {
            ExportProperty(nameof(BoundAssignEx), x, nameof(BoundAssignEx.Target), x.Target);
            Accept(x.Target);

            ExportProperty(nameof(BoundAssignEx), x, nameof(BoundAssignEx.Value), x.Value);
            Accept(x.Value);

            return default;
        }

        public override VoidStruct VisitConditional(BoundConditionalEx x)
        {
            ExportProperty(nameof(BoundConditionalEx), x, nameof(BoundConditionalEx.Condition), x.Condition);
            Accept(x.Condition);

            ExportProperty(nameof(BoundConditionalEx), x, nameof(BoundConditionalEx.IfTrue), x.IfTrue);
            Accept(x.IfTrue);

            ExportProperty(nameof(BoundConditionalEx), x, nameof(BoundConditionalEx.IfFalse), x.IfFalse);
            Accept(x.IfFalse);

            return default;
        }

        public override VoidStruct VisitAssign(BoundAssignEx x)
        {
            ExportProperty(nameof(BoundAssignEx), x, nameof(BoundAssignEx.Target), x.Target);
            Accept(x.Target);

            ExportProperty(nameof(BoundAssignEx), x, nameof(BoundAssignEx.Value), x.Value);
            Accept(x.Value);

            return default;
        }

        public override VoidStruct VisitCompoundAssign(BoundCompoundAssignEx x)
        {
            ExportProperty(nameof(BoundAssignEx), x, nameof(BoundAssignEx.Target), x.Target);
            Accept(x.Target);

            ExportProperty(nameof(BoundAssignEx), x, nameof(BoundAssignEx.Value), x.Value);
            Accept(x.Value);

            return default;
        }

        public override VoidStruct VisitVariableName(BoundVariableName x)
        {
            ExportProperty(nameof(BoundVariableName), x, nameof(BoundVariableName.NameExpression), x.NameExpression);
            Accept(x.NameExpression);

            return default;
        }

        public override VoidStruct VisitVariableRef(BoundVariableRef x)
        {
            ExportProperty(nameof(BoundVariableRef), x, nameof(BoundVariableRef.Name), x.Name);
            Accept(x.Name);

            return default;
        }

        public override VoidStruct VisitTemporalVariableRef(BoundTemporalVariableRef x)
        {
            // BoundSynthesizedVariableRef is based solely on BoundVariableRef so far 
            VisitVariableRef(x);

            return default;
        }

        public override VoidStruct VisitList(BoundListEx x)
        {
            // TODO: Export
            x.Items.ForEach(pair =>
            {
                Accept(pair.Key);
                Accept(pair.Value);
            });

            return default;
        }

        public override VoidStruct VisitFieldRef(BoundFieldRef x)
        {
            //ExportProperty(nameof(BoundFieldRef), x, nameof(BoundFieldRef.ContainingType), x.ContainingType);
            Accept(x.ContainingType);

            ExportProperty(nameof(BoundFieldRef), x, nameof(BoundFieldRef.Instance), x.Instance);
            Accept(x.Instance);

            ExportProperty(nameof(BoundFieldRef), x, nameof(BoundFieldRef.FieldName), x.FieldName);
            Accept(x.FieldName);

            return default;
        }

        public override VoidStruct VisitArray(BoundArrayEx x)
        {
            // TODO: Export
            x.Items.ForEach(pair =>
            {
                Accept(pair.Key);
                Accept(pair.Value);
            });

            return default;
        }

        public override VoidStruct VisitArrayItem(BoundArrayItemEx x)
        {
            ExportProperty(nameof(BoundArrayItemEx), x, nameof(BoundArrayItemEx.Array), x.Array);
            Accept(x.Array);

            ExportProperty(nameof(BoundArrayItemEx), x, nameof(BoundArrayItemEx.Index), x.Index);
            Accept(x.Index);

            return default;
        }

        public override VoidStruct VisitArrayItemOrd(BoundArrayItemOrdEx x)
        {
            ExportProperty(nameof(BoundArrayItemEx), x, nameof(BoundArrayItemEx.Array), x.Array);
            Accept(x.Array);

            ExportProperty(nameof(BoundArrayItemEx), x, nameof(BoundArrayItemEx.Index), x.Index);
            Accept(x.Index);

            return default;
        }

        public override VoidStruct VisitInstanceOf(BoundInstanceOfEx x)
        {
            ExportProperty(nameof(BoundInstanceOfEx), x, nameof(BoundInstanceOfEx.Operand), x.Operand);
            Accept(x.Operand);

            //ExportProperty(nameof(BoundInstanceOfEx), x, nameof(BoundInstanceOfEx.AsType), x.AsType);
            Accept(x.AsType);

            return default;
        }

        public override VoidStruct VisitGlobalConstUse(BoundGlobalConst x)
        {
            return default;
        }

        public override VoidStruct VisitGlobalConstDecl(BoundGlobalConstDeclStatement x)
        {
            ExportProperty(nameof(BoundGlobalConstDeclStatement), x, nameof(BoundGlobalConstDeclStatement.Value), x.Value);
            Accept(x.Value);

            return default;
        }

        public override VoidStruct VisitPseudoConstUse(BoundPseudoConst x)
        {
            return default;
        }

        public override VoidStruct VisitPseudoClassConstUse(BoundPseudoClassConst x)
        {
            //ExportProperty(nameof(BoundPseudoClassConst), x, nameof(BoundPseudoClassConst.TargetType), x.TargetType);
            Accept(x.TargetType);

            return default;
        }

        public override VoidStruct VisitIsEmpty(BoundIsEmptyEx x)
        {
            ExportProperty(nameof(BoundIsEmptyEx), x, nameof(BoundIsEmptyEx.Operand), x.Operand);
            Accept(x.Operand);

            return default;
        }

        public override VoidStruct VisitIsSet(BoundIsSetEx x)
        {
            ExportProperty(nameof(BoundIsSetEx), x, nameof(BoundIsSetEx.VarReference), x.VarReference);
            Accept(x.VarReference);

            return default;
        }

        public override VoidStruct VisitOffsetExists(BoundOffsetExists x)
        {
            ExportProperty(nameof(BoundOffsetExists), x, nameof(BoundOffsetExists.Receiver), x.Receiver);
            Accept(x.Receiver);

            ExportProperty(nameof(BoundOffsetExists), x, nameof(BoundOffsetExists.Index), x.Index);
            Accept(x.Index);

            return default;
        }

        public override VoidStruct VisitTryGetItem(BoundTryGetItem x)
        {
            ExportProperty(nameof(BoundTryGetItem), x, nameof(BoundTryGetItem.Array), x.Array);
            Accept(x.Array);

            ExportProperty(nameof(BoundTryGetItem), x, nameof(BoundTryGetItem.Index), x.Index);
            Accept(x.Index);

            ExportProperty(nameof(BoundTryGetItem), x, nameof(BoundTryGetItem.Fallback), x.Fallback);
            Accept(x.Fallback);

            return default;
        }

        public override VoidStruct VisitLambda(BoundLambda x)
        {
            return default;
        }

        public override VoidStruct VisitEval(BoundEvalEx x)
        {
            ExportProperty(nameof(BoundEvalEx), x, nameof(BoundEvalEx.CodeExpression), x.CodeExpression);
            Accept(x.CodeExpression);

            return default;
        }


        public override VoidStruct VisitYieldEx(BoundYieldEx boundYieldEx)
        {
            return default;
        }

        public override VoidStruct VisitYieldFromEx(BoundYieldFromEx x)
        {
            ExportProperty(nameof(BoundYieldFromEx), x, nameof(BoundYieldFromEx.Operand), x.Operand);
            Accept(x.Operand);

            return default;
        }

        #endregion

        #region Statements

        public override VoidStruct VisitUnset(BoundUnset x)
        {
            ExportProperty(nameof(BoundUnset), x, nameof(BoundUnset.Variable), x.Variable);
            Accept(x.Variable);

            return default;
        }

        public override VoidStruct VisitEmptyStatement(BoundEmptyStatement x)
        {
            return default;
        }

        public override VoidStruct VisitBlockStatement(BoundBlock x)
        {
            Debug.Assert(x.NextEdge == null);

            for (int i = 0; i < x.Statements.Count; i++)
            {
                ExportPropertyItem(nameof(BoundBlock), x, nameof(BoundBlock.Statements), i, x.Statements[i]);
                Accept(x.Statements[i]);
            }

            return default;
        }

        public override VoidStruct VisitExpressionStatement(BoundExpressionStatement x)
        {
            ExportProperty(nameof(BoundExpressionStatement), x, nameof(BoundExpressionStatement.Expression), x.Expression);
            Accept(x.Expression);

            return default;
        }

        public override VoidStruct VisitReturn(BoundReturnStatement x)
        {
            ExportProperty(nameof(BoundReturnStatement), x, nameof(BoundReturnStatement.Returned), x.Returned);
            Accept(x.Returned);

            return default;
        }

        public override VoidStruct VisitThrow(BoundThrowStatement x)
        {
            ExportProperty(nameof(BoundThrowStatement), x, nameof(BoundThrowStatement.Thrown), x.Thrown);
            Accept(x.Thrown);

            return default;
        }

        public override VoidStruct VisitFunctionDeclaration(BoundFunctionDeclStatement x)
        {
            return default;
        }

        public override VoidStruct VisitTypeDeclaration(BoundTypeDeclStatement x)
        {
            return default;
        }

        public override VoidStruct VisitGlobalStatement(BoundGlobalVariableStatement x)
        {
            ExportProperty(nameof(BoundGlobalVariableStatement), x, nameof(BoundGlobalVariableStatement.Variable), x.Variable);
            Accept(x.Variable);

            return default;
        }

        public override VoidStruct VisitStaticStatement(BoundStaticVariableStatement x)
        {
            return default;
        }

        public override VoidStruct VisitYieldStatement(BoundYieldStatement x)
        {
            ExportProperty(nameof(BoundYieldStatement), x, nameof(BoundYieldStatement.YieldedValue), x.YieldedValue);
            Accept(x.YieldedValue);

            ExportProperty(nameof(BoundYieldStatement), x, nameof(BoundYieldStatement.YieldedKey), x.YieldedKey);
            Accept(x.YieldedKey);

            return default;
        }

        public override VoidStruct VisitDeclareStatement(BoundDeclareStatement x)
        {
            return default;
        }

        #endregion
    }
}
