using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Pchp.CodeAnalysis.Semantics.Graph;
using Pchp.CodeAnalysis.Symbols;
using Peachpie.CodeAnalysis.Utilities;

namespace Pchp.CodeAnalysis.FlowAnalysis.Passes
{
    internal class SouffleEncoder : GraphExplorer<VoidStruct>
    {
        private readonly SourceRoutineSymbol _routine;
        private readonly TextWriter _blockFollows;

        private SouffleEncoder(SourceRoutineSymbol routine, TextWriter blockFollows)
        {
            _routine = routine;
            _blockFollows = blockFollows;
        }

        protected override void DefaultVisitUnexploredBlock(BoundBlock x)
        {
            string thisBlockName = GetUniqueBlockName(_routine, x);
            foreach (var nextBlock in x.NextEdge?.Targets ?? Enumerable.Empty<BoundBlock>())
            {
                string nextBlockName = GetUniqueBlockName(_routine, nextBlock);
                _blockFollows.WriteLine(thisBlockName + "\t" + nextBlockName);
            }

            base.DefaultVisitUnexploredBlock(x);
        }

        public static void Encode(PhpCompilation compilation, string basePath)
        {
            using var start = new StreamWriter(Path.Combine(basePath, "Start.facts"), false);
            using var blockFollows = new StreamWriter(Path.Combine(basePath, "BlockFollows.facts"), false);
            using var syntacticallyUnreachable = new StreamWriter(Path.Combine(basePath, "SyntacticallyUnreachable.facts"), false);

            foreach (var routine in compilation.SourceSymbolCollection.AllRoutines)
            {
                if (routine.ControlFlowGraph == null)
                    continue;

                start.WriteLine(GetUniqueBlockName(routine, routine.ControlFlowGraph.Start));

                var encoder = new SouffleEncoder(routine, blockFollows);
                routine.ControlFlowGraph.Accept(encoder);

                foreach (var unreachableBlock in routine.ControlFlowGraph.UnreachableBlocks)
                {
                    syntacticallyUnreachable.WriteLine(GetUniqueBlockName(routine, unreachableBlock));
                    unreachableBlock.Accept(encoder);
                }
            }
        }

        private static string GetUniqueBlockName(SourceRoutineSymbol routine, BoundBlock block)
        {
            return $"{routine.ContainingType.GetFullName()}.{routine.MetadataName}:{block.DebugName}#{block.Ordinal}";
        }
    }
}
