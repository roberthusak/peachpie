using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
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
    }
}
