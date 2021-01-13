using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
using Pchp.CodeAnalysis.Symbols;

namespace Peachpie.CodeAnalysis.FlowAnalysis.Graph
{
    readonly struct CallSite : IEquatable<CallSite>
    {
        public CallSite(BoundBlock block, BoundRoutineCall callExpression)
        {
            Block = block;
            CallExpression = callExpression;
        }

        public BoundBlock Block { get; }

        public BoundRoutineCall CallExpression { get; }

        public bool Equals(CallSite other)
        {
            return ReferenceEquals(Block, other.Block) && ReferenceEquals(CallExpression, other.CallExpression);
        }

        public override bool Equals(object obj)
        {
            return obj is CallSite other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Block != null ? Block.GetHashCode() : 0) * 397) ^ (CallExpression != null ? CallExpression.GetHashCode() : 0);
            }
        }
    }

    /// <summary>
    /// Stores the information about the calls among the routines in source code. This class is thread-safe.
    /// </summary>
    class CallGraph
    {
        /// <summary>
        /// Maps each node to its incident edges, their directions can be found by <see cref="Edge.Caller"/>
        /// and <see cref="Edge.Callee"/>.
        /// </summary>
        private readonly ConcurrentDictionary<SourceRoutineSymbol, HashSet<Edge>> _incidentEdges;

        public CallGraph()
        {
            _incidentEdges = new ConcurrentDictionary<SourceRoutineSymbol, HashSet<Edge>>();
        }

        public Edge AddEdge(SourceRoutineSymbol caller, SourceRoutineSymbol callee, CallSite callSite)
        {
            var edge = new Edge(caller, callee, callSite);
            AddRoutineEdge(caller, edge);
            AddRoutineEdge(callee, edge);

            return edge;
        }

        public IEnumerable<Edge> GetIncidentEdges(SourceRoutineSymbol routine)
        {
            if (_incidentEdges.TryGetValue(routine, out var edges))
            {
                lock (edges)
                {
                    return edges.ToArray();
                }
            }
            else
            {
                return Array.Empty<Edge>();
            }
        }

        public IEnumerable<Edge> GetCalleeEdges(SourceRoutineSymbol caller)
        {
            return GetIncidentEdges(caller).Where(edge => edge.Caller == caller);
        }

        public IEnumerable<Edge> GetCallerEdges(SourceRoutineSymbol callee)
        {
            return GetIncidentEdges(callee).Where(edge => edge.Callee == callee);
        }

        private void AddRoutineEdge(SourceRoutineSymbol routine, Edge edge)
        {
            _incidentEdges.AddOrUpdate(
                routine,
                _ => new HashSet<Edge>() { edge },
                (_, edges) =>
                {
                    lock (edges)
                    {
                        edges.Add(edge);
                        return edges;
                    }
                });
        }

        public class Edge : IEquatable<Edge>
        {
            public Edge(SourceRoutineSymbol caller, SourceRoutineSymbol callee, CallSite callSite)
            {
                Caller = caller;
                Callee = callee;
                CallSite = callSite;
            }

            public SourceRoutineSymbol Caller { get; }

            public SourceRoutineSymbol Callee { get; }

            public CallSite CallSite { get; }

            public bool Equals(Edge other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return ReferenceEquals(Caller, other.Caller) && ReferenceEquals(Callee, other.Callee) && CallSite.Equals(other.CallSite);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (!(obj is Edge edge)) return false;
                return Equals(edge);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (Caller != null ? Caller.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (Callee != null ? Callee.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ CallSite.GetHashCode();
                    return hashCode;
                }
            }
        }
    }
}
