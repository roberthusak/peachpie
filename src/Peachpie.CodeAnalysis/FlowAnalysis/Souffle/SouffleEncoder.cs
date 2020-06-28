using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Devsense.PHP.Syntax;
using Devsense.PHP.Syntax.Ast;
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
        private readonly Stack<object> _nodeStack = new Stack<object>();

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
                BoundBlock block => $"{block.GetType().Name}#{block.SerialNumber}",
                Edge edge => $"{edge.GetType().Name}#{edge.SerialNumber}",
                IPhpOperation phpOp => $"{GetPhpOperationName(phpOp)}#{((BoundOperation)phpOp).SerialNumber}",
                BoundOperation op => $"{op.GetType().Name}#{op.SerialNumber}",
                VariableName varName => varName.Value,
                string pseudoNode => pseudoNode,
                _ => throw new NotSupportedException()
            };

        private static string GetPhpOperationName(IPhpOperation op)
        {
            string FormatCode(string str)
            {
                str = str.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');     // \t is used as a separator in relation rows
                if (str.Length > 30)
                {
                    str = str.Substring(0, 30) + "...";
                }

                return str;
            }

            string sourceCode = op.PhpSyntax?.ContainingSourceUnit?.GetSourceCode(op.PhpSyntax.Span);
            return (sourceCode != null) ? FormatCode(sourceCode) : op.GetType().Name;
        }

        private void Export(object node)
        {
            var nodeName = GetName(node);

            // Export the type relation
            var type = node.GetType();
            _writers.WriteType(type.Name, nodeName);

            ExportCommonNodeProperties(node, nodeName);
        }

        private string ExportPseudoNode(SouffleType type, string label)
        {
            int id = BoundOperation.GetFreeSerialNumber();
            string nodeName = $"{label}#{id}";

            // TODO: Export pseudo Is_* relation if needed

            ExportCommonNodeProperties(nodeName, nodeName);

            return nodeName;
        }

        private void ExportCommonNodeProperties(object node, string nodeName)
        {
            // Export that it's contained in the routine
            _writers.WriteRoutineNode(_routineName, nodeName);

            // Export the Next relation
            if (_nodeStack.Count > 0)
            {
                ExportNext(_nodeStack.Peek(), node);
            }

            _nodeStack.Push(node);
        }

        private void RollbackExportStack(object node)
        {
            while (_nodeStack.Count > 0 && _nodeStack.Peek() != node)
            {
                _nodeStack.Pop();
            }
        }

        private object GetLastExported() => _nodeStack.Peek();

        private void ExportNext(object from, object to) => _writers.WriteNext(GetName(from), GetName(to));

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

        public override VoidStruct VisitCFGBlock(BoundBlock x)
        {
            Export(x);

            DefaultVisitBlock(x);

            return default;
        }

        public override VoidStruct VisitCFGStartBlock(StartBlock x)
        {
            Export(x);

            foreach (var parameter in _routine.SourceParameters)
            {
                ExportPseudoNode(SouffleUtils.ParameterPassType, $"pass ${parameter.Name}");
            }

            DefaultVisitBlock(x);

            return default;
        }

        public override VoidStruct VisitCFGExitBlock(ExitBlock x)
        {
            Export(x);

            return DefaultVisitBlock(x);
        }

        public override VoidStruct VisitCFGCatchBlock(CatchBlock x)
        {
            Export(x);

            //ExportProperty(nameof(CatchBlock), x, nameof(CatchBlock.TypeRef), x.TypeRef);
            Accept(x.TypeRef);

            ExportProperty(nameof(CatchBlock), x, nameof(CatchBlock.Variable), x.Variable);
            Accept(x.Variable);

            DefaultVisitBlock(x);

            return default;
        }

        public override VoidStruct VisitCFGCaseBlock(CaseBlock x)
        {
            Export(x);

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
            Export(x);

            Debug.Assert(x.NextBlock != null && x.NextBlock == x.Target);

            ExportProperty(nameof(SimpleEdge), x, nameof(SimpleEdge.Target), x.Target);
            x.NextBlock.Accept(this);

            DefaultVisitEdge(x);

            return default;
        }

        public override VoidStruct VisitCFGConditionalEdge(ConditionalEdge x)
        {
            Export(x);

            Accept(x.Condition);

            var lastConditionNode = GetLastExported();

            ExportProperty(nameof(ConditionalEdge), x, nameof(ConditionalEdge.TrueTarget), x.TrueTarget);
            x.TrueTarget.Accept(this);

            RollbackExportStack(lastConditionNode);

            ExportProperty(nameof(ConditionalEdge), x, nameof(ConditionalEdge.FalseTarget), x.FalseTarget);
            x.FalseTarget.Accept(this);

            return default;
        }

        public override VoidStruct VisitCFGTryCatchEdge(TryCatchEdge x)
        {
            Export(x);

            // TODO: Capture that any thrown exception may lead directly to a catch block
            ExportProperty(nameof(TryCatchEdge), x, nameof(TryCatchEdge.BodyBlock), x.BodyBlock);
            x.BodyBlock.Accept(this);

            var lastBodyNode = GetLastExported();
            ExportPropertyEnumerable(nameof(Edge), x, nameof(Edge.CatchBlocks), x.CatchBlocks);
            for (int i = 0; i < x.CatchBlocks.Length; i++)
            {
                var catchBlock = x.CatchBlocks[i];
                catchBlock.Accept(this);

                if (x.FinallyBlock != null)
                {
                    ExportNext(GetLastExported(), x.FinallyBlock);
                }

                RollbackExportStack(lastBodyNode);
            }

            if (x.FinallyBlock != null)
            {
                ExportProperty(nameof(Edge), x, nameof(TryCatchEdge.FinallyBlock), x.FinallyBlock);
                x.FinallyBlock.Accept(this);
            }

            return default;
        }

        public override VoidStruct VisitCFGForeachEnumereeEdge(ForeachEnumereeEdge x)
        {
            Export(x);

            ExportProperty(nameof(ForeachEnumereeEdge), x, nameof(ForeachEnumereeEdge.Enumeree), x.Enumeree);
            Accept(x.Enumeree);

            ExportProperty(nameof(SimpleEdge), x, nameof(SimpleEdge.Target), x.Target);
            x.NextBlock.Accept(this);

            return default;
        }

        public override VoidStruct VisitCFGForeachMoveNextEdge(ForeachMoveNextEdge x)
        {
            Export(x);

            ExportProperty(nameof(ForeachMoveNextEdge), x, nameof(ForeachMoveNextEdge.ValueVariable), x.ValueVariable);
            Accept(x.ValueVariable);

            ExportProperty(nameof(ForeachMoveNextEdge), x, nameof(ForeachMoveNextEdge.KeyVariable), x.KeyVariable);
            Accept(x.KeyVariable);

            ExportProperty(nameof(ForeachMoveNextEdge), x, nameof(ForeachMoveNextEdge.BodyBlock), x.BodyBlock);
            x.BodyBlock.Accept(this);

            RollbackExportStack(x);

            ExportProperty(nameof(Edge), x, nameof(Edge.NextBlock), x.NextBlock);
            x.NextBlock.Accept(this);

            return default;
        }

        public override VoidStruct VisitCFGSwitchEdge(SwitchEdge x)
        {
            Export(x);

            ExportProperty(nameof(SwitchEdge), x, nameof(SwitchEdge.SwitchValue), x.SwitchValue);
            Accept(x.SwitchValue);

            var lastValueNode = GetLastExported();

            //
            ExportPropertyEnumerable(nameof(Edge), x, nameof(Edge.CaseBlocks), x.CaseBlocks);
            var arr = x.CaseBlocks;
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i].Accept(this);

                RollbackExportStack(lastValueNode);
            }

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

            Export(x);
            return default;
        }

        public override VoidStruct VisitLiteral(BoundLiteral x)
        {
            //VisitLiteralExpression(x);

            Export(x);
            return default;
        }

        public override VoidStruct VisitCopyValue(BoundCopyValue x)
        {
            ExportProperty(nameof(BoundCopyValue), x, nameof(BoundCopyValue.Expression), x.Expression);
            Accept(x.Expression);

            Export(x);
            return default;
        }

        public override VoidStruct VisitArgument(BoundArgument x)
        {
            ExportProperty(nameof(BoundArgument), x, nameof(BoundArgument.Value), x.Value);
            Accept(x.Value);

            Export(x);
            return default;
        }

        internal override VoidStruct VisitTypeRef(BoundTypeRef x)
        {
            Export(x);
            return base.VisitTypeRef(x);
        }

        internal override VoidStruct VisitIndirectTypeRef(BoundIndirectTypeRef x)
        {
            ExportProperty(nameof(BoundIndirectTypeRef), x, nameof(BoundIndirectTypeRef.TypeExpression), x.TypeExpression);
            Accept(x.TypeExpression);

            Export(x);
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

            Export(x);
            return default;
        }

        public override VoidStruct VisitRoutineName(BoundRoutineName x)
        {
            ExportProperty(nameof(BoundRoutineName), x, nameof(BoundRoutineName.NameExpression), x.NameExpression);
            Accept(x.NameExpression);

            Export(x);
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

            // Handle short-circuit evaluation of && and ||
            if (x.Operation == Operations.And || x.Operation == Operations.Or)
            {
                ExportNext(GetLastExported(), x);
            }

            ExportProperty(nameof(BoundBinaryEx), x, nameof(BoundBinaryEx.Right), x.Right);
            Accept(x.Right);

            Export(x);
            return default;
        }

        public override VoidStruct VisitUnaryExpression(BoundUnaryEx x)
        {
            ExportProperty(nameof(BoundUnaryEx), x, nameof(BoundUnaryEx.Operand), x.Operand);
            Accept(x.Operand);

            Export(x);
            return default;
        }

        public override VoidStruct VisitConversion(BoundConversionEx x)
        {
            ExportProperty(nameof(BoundConversionEx), x, nameof(BoundConversionEx.Operand), x.Operand);
            Accept(x.Operand);

            ExportProperty(nameof(BoundConversionEx), x, nameof(BoundConversionEx.TargetType), x.TargetType);
            Accept(x.TargetType);

            Export(x);
            return default;
        }

        public override VoidStruct VisitIncDec(BoundIncDecEx x)
        {
            ExportProperty(nameof(BoundAssignEx), x, nameof(BoundAssignEx.Target), x.Target);
            Accept(x.Target);

            ExportProperty(nameof(BoundAssignEx), x, nameof(BoundAssignEx.Value), x.Value);
            Accept(x.Value);

            Export(x);
            return default;
        }

        public override VoidStruct VisitConditional(BoundConditionalEx x)
        {
            ExportProperty(nameof(BoundConditionalEx), x, nameof(BoundConditionalEx.Condition), x.Condition);
            Accept(x.Condition);

            var lastConditionNode = GetLastExported();

            ExportProperty(nameof(BoundConditionalEx), x, nameof(BoundConditionalEx.IfTrue), x.IfTrue);
            Accept(x.IfTrue);

            ExportNext(GetLastExported(), x);
            RollbackExportStack(lastConditionNode);

            ExportProperty(nameof(BoundConditionalEx), x, nameof(BoundConditionalEx.IfFalse), x.IfFalse);
            Accept(x.IfFalse);

            Export(x);
            return default;
        }

        public override VoidStruct VisitAssign(BoundAssignEx x)
        {
            ExportProperty(nameof(BoundAssignEx), x, nameof(BoundAssignEx.Target), x.Target);
            Accept(x.Target);

            ExportProperty(nameof(BoundAssignEx), x, nameof(BoundAssignEx.Value), x.Value);
            Accept(x.Value);

            Export(x);
            return default;
        }

        public override VoidStruct VisitCompoundAssign(BoundCompoundAssignEx x)
        {
            ExportProperty(nameof(BoundAssignEx), x, nameof(BoundAssignEx.Target), x.Target);
            Accept(x.Target);

            ExportProperty(nameof(BoundAssignEx), x, nameof(BoundAssignEx.Value), x.Value);
            Accept(x.Value);

            Export(x);
            return default;
        }

        public override VoidStruct VisitVariableName(BoundVariableName x)
        {
            ExportProperty(nameof(BoundVariableName), x, nameof(BoundVariableName.NameExpression), x.NameExpression);
            Accept(x.NameExpression);

            ExportProperty(nameof(BoundVariableName), x, nameof(BoundVariableName.NameValue), x.NameValue);

            Export(x);
            return default;
        }

        public override VoidStruct VisitVariableRef(BoundVariableRef x)
        {
            ExportProperty(nameof(BoundVariableRef), x, nameof(BoundVariableRef.Name), x.Name);
            Accept(x.Name);

            Export(x);
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

            Export(x);
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

            Export(x);
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

            Export(x);
            return default;
        }

        public override VoidStruct VisitArrayItem(BoundArrayItemEx x)
        {
            ExportProperty(nameof(BoundArrayItemEx), x, nameof(BoundArrayItemEx.Array), x.Array);
            Accept(x.Array);

            ExportProperty(nameof(BoundArrayItemEx), x, nameof(BoundArrayItemEx.Index), x.Index);
            Accept(x.Index);

            Export(x);
            return default;
        }

        public override VoidStruct VisitArrayItemOrd(BoundArrayItemOrdEx x)
        {
            ExportProperty(nameof(BoundArrayItemEx), x, nameof(BoundArrayItemEx.Array), x.Array);
            Accept(x.Array);

            ExportProperty(nameof(BoundArrayItemEx), x, nameof(BoundArrayItemEx.Index), x.Index);
            Accept(x.Index);

            Export(x);
            return default;
        }

        public override VoidStruct VisitInstanceOf(BoundInstanceOfEx x)
        {
            ExportProperty(nameof(BoundInstanceOfEx), x, nameof(BoundInstanceOfEx.Operand), x.Operand);
            Accept(x.Operand);

            //ExportProperty(nameof(BoundInstanceOfEx), x, nameof(BoundInstanceOfEx.AsType), x.AsType);
            Accept(x.AsType);

            Export(x);
            return default;
        }

        public override VoidStruct VisitGlobalConstUse(BoundGlobalConst x)
        {
            Export(x);
            return default;
        }

        public override VoidStruct VisitGlobalConstDecl(BoundGlobalConstDeclStatement x)
        {
            ExportProperty(nameof(BoundGlobalConstDeclStatement), x, nameof(BoundGlobalConstDeclStatement.Value), x.Value);
            Accept(x.Value);

            Export(x);
            return default;
        }

        public override VoidStruct VisitPseudoConstUse(BoundPseudoConst x)
        {
            Export(x);
            return default;
        }

        public override VoidStruct VisitPseudoClassConstUse(BoundPseudoClassConst x)
        {
            //ExportProperty(nameof(BoundPseudoClassConst), x, nameof(BoundPseudoClassConst.TargetType), x.TargetType);
            Accept(x.TargetType);

            Export(x);
            return default;
        }

        public override VoidStruct VisitIsEmpty(BoundIsEmptyEx x)
        {
            ExportProperty(nameof(BoundIsEmptyEx), x, nameof(BoundIsEmptyEx.Operand), x.Operand);
            Accept(x.Operand);

            Export(x);
            return default;
        }

        public override VoidStruct VisitIsSet(BoundIsSetEx x)
        {
            ExportProperty(nameof(BoundIsSetEx), x, nameof(BoundIsSetEx.VarReference), x.VarReference);
            Accept(x.VarReference);

            Export(x);
            return default;
        }

        public override VoidStruct VisitOffsetExists(BoundOffsetExists x)
        {
            ExportProperty(nameof(BoundOffsetExists), x, nameof(BoundOffsetExists.Receiver), x.Receiver);
            Accept(x.Receiver);

            ExportProperty(nameof(BoundOffsetExists), x, nameof(BoundOffsetExists.Index), x.Index);
            Accept(x.Index);

            Export(x);
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

            Export(x);
            return default;
        }

        public override VoidStruct VisitLambda(BoundLambda x)
        {
            Export(x);
            return default;
        }

        public override VoidStruct VisitEval(BoundEvalEx x)
        {
            ExportProperty(nameof(BoundEvalEx), x, nameof(BoundEvalEx.CodeExpression), x.CodeExpression);
            Accept(x.CodeExpression);

            Export(x);
            return default;
        }


        public override VoidStruct VisitYieldEx(BoundYieldEx x)
        {
            Export(x);
            return default;
        }

        public override VoidStruct VisitYieldFromEx(BoundYieldFromEx x)
        {
            ExportProperty(nameof(BoundYieldFromEx), x, nameof(BoundYieldFromEx.Operand), x.Operand);
            Accept(x.Operand);

            Export(x);
            return default;
        }

        #endregion

        #region Statements

        public override VoidStruct VisitUnset(BoundUnset x)
        {
            ExportProperty(nameof(BoundUnset), x, nameof(BoundUnset.Variable), x.Variable);
            Accept(x.Variable);

            Export(x);
            return default;
        }

        public override VoidStruct VisitEmptyStatement(BoundEmptyStatement x)
        {
            Export(x);
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

            Export(x);
            return default;
        }

        public override VoidStruct VisitExpressionStatement(BoundExpressionStatement x)
        {
            ExportProperty(nameof(BoundExpressionStatement), x, nameof(BoundExpressionStatement.Expression), x.Expression);
            Accept(x.Expression);

            Export(x);
            return default;
        }

        public override VoidStruct VisitReturn(BoundReturnStatement x)
        {
            ExportProperty(nameof(BoundReturnStatement), x, nameof(BoundReturnStatement.Returned), x.Returned);
            Accept(x.Returned);

            Export(x);
            return default;
        }

        public override VoidStruct VisitThrow(BoundThrowStatement x)
        {
            ExportProperty(nameof(BoundThrowStatement), x, nameof(BoundThrowStatement.Thrown), x.Thrown);
            Accept(x.Thrown);

            Export(x);
            return default;
        }

        public override VoidStruct VisitFunctionDeclaration(BoundFunctionDeclStatement x)
        {
            Export(x);
            return default;
        }

        public override VoidStruct VisitTypeDeclaration(BoundTypeDeclStatement x)
        {
            Export(x);
            return default;
        }

        public override VoidStruct VisitGlobalStatement(BoundGlobalVariableStatement x)
        {
            ExportProperty(nameof(BoundGlobalVariableStatement), x, nameof(BoundGlobalVariableStatement.Variable), x.Variable);
            Accept(x.Variable);

            Export(x);
            return default;
        }

        public override VoidStruct VisitStaticStatement(BoundStaticVariableStatement x)
        {
            Export(x);
            return default;
        }

        public override VoidStruct VisitYieldStatement(BoundYieldStatement x)
        {
            ExportProperty(nameof(BoundYieldStatement), x, nameof(BoundYieldStatement.YieldedValue), x.YieldedValue);
            Accept(x.YieldedValue);

            ExportProperty(nameof(BoundYieldStatement), x, nameof(BoundYieldStatement.YieldedKey), x.YieldedKey);
            Accept(x.YieldedKey);

            Export(x);
            return default;
        }

        public override VoidStruct VisitDeclareStatement(BoundDeclareStatement x)
        {
            Export(x);
            return default;
        }

        #endregion
    }
}
