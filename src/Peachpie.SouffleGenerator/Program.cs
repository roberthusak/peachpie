using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
using Pchp.CodeAnalysis.FlowAnalysis.Souffle;

namespace Peachpie.SouffleGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("The location of the output Soufflé file needed.");
                return;
            }

            using var writer = new StreamWriter(File.Open(args[0], FileMode.Create));
            GenerateTypes(writer);
        }

        private static void GenerateTypes(TextWriter writer)
        {
            var basicTypes =
                SouffleUtils.ExportedTypes
                .Where(t => !t.IsAbstract)
                .OrderBy(t => t.Name)
                .ToArray();

            // Basic types
            foreach (var opType in basicTypes)
            {
                string name = SouffleUtils.GetTypeName(opType, isBase: false);
                writer.WriteLine($".type {name} <: symbol");
            }

            writer.WriteLine();

            // "Routine" type for unique routine names
            writer.WriteLine(SouffleUtils.RoutineType.GetDeclaration());

            // Pseudo type for parameter passing
            writer.WriteLine(SouffleUtils.ParameterPassType.GetDeclaration());

            writer.WriteLine();

            // Turn inheritance into union hierarchy
            foreach (var opType in SouffleUtils.ExportedUnionTypes.OrderBy(t => t.Name))
            {
                var subTypes =
                    SouffleUtils.ExportedTypes
                    .Where(t => t.BaseType == opType)
                    .OrderBy(t => t.Name)
                    .Select(t => SouffleUtils.GetTypeName(t))
                    .ToArray();

                if (!opType.IsAbstract)
                {
                    subTypes = subTypes.Prepend(SouffleUtils.GetTypeName(opType, isBase: false)).ToArray();
                }

                string name = SouffleUtils.GetTypeName(opType, isBase: true);
                writer.WriteLine($".type {name} = " + string.Join(" | ", (object[])subTypes));
            }

            // "Node" type unifying operations and edges
            writer.WriteLine(SouffleUtils.NodeType.GetDeclaration());

            writer.WriteLine();

            // Type relations
            foreach (var opType in SouffleUtils.ExportedTypes.OrderBy(t => t.Name))
            {
                var typeRelation = SouffleUtils.ExportedTypeRelations[opType];
                writer.WriteLine(typeRelation.GetDeclaration());

                if (!opType.IsAbstract)
                {
                    // Non-abstract types are supplied directly, may be enriched from inherited classes
                    writer.WriteLine($".input {typeRelation.Name}");
                }

                if (SouffleUtils.ExportedUnionTypes.Contains(opType))
                {
                    // Infer from the relations of inherited classes
                    var subTypeRelations =
                        SouffleUtils.ExportedTypes
                        .Where(t => t.BaseType == opType)
                        .OrderBy(t => t.Name)
                        .Select(t => SouffleUtils.ExportedTypeRelations[t].Name + "(n)")
                        .ToArray();
                    writer.WriteLine($"{typeRelation.Name}(n) :- {string.Join("; ", subTypeRelations)}.");
                }
            }

            writer.WriteLine();

            // Custom relations

            writer.WriteLine(SouffleUtils.RoutineNodeRelation.GetDeclaration());
            writer.WriteLine($".input {SouffleUtils.RoutineNodeRelation.Name}");

            writer.WriteLine(SouffleUtils.NextRelation.GetDeclaration());
            writer.WriteLine($".input {SouffleUtils.NextRelation.Name}");

            writer.WriteLine(SouffleUtils.ParameterPassNameRelation.GetDeclaration());
            writer.WriteLine($".input {SouffleUtils.ParameterPassNameRelation.Name}");

            writer.WriteLine();

            var props =
                SouffleUtils.ExportedProperties.Values
                .OrderBy(p => p.Name)
                .ToArray();

            // Turn properties containing operation types into relations
            foreach (var relation in props)
            {
                writer.WriteLine(relation.GetDeclaration());
                writer.WriteLine($".input {relation.Name}");
            }
        }
    }
}
