﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
using Peachpie.CodeAnalysis.FlowAnalysis.Souffle;

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
            // Basic types
            foreach (var opType in SouffleUtils.ExportedTypes.Where(t => !t.IsAbstract))
            {
                string name = SouffleUtils.GetOperationTypeName(opType, isBase: false);
                writer.WriteLine($".type {name} <: symbol");
            }

            writer.WriteLine();

            // Turn inheritance into union hierarchy
            foreach (var opType in SouffleUtils.ExportedUnionTypes)
            {
                var subTypes =
                    SouffleUtils.ExportedTypes
                    .Where(t => t.BaseType == opType)
                    .Select(t => SouffleUtils.GetOperationTypeName(t))
                    .ToArray();

                if (!opType.IsAbstract)
                {
                    subTypes = subTypes.Prepend(SouffleUtils.GetOperationTypeName(opType, isBase: false)).ToArray();
                }

                string name = SouffleUtils.GetOperationTypeName(opType, isBase: true);
                writer.WriteLine($".type {name} = " + string.Join(" | ", (object[])subTypes));
            }

            writer.WriteLine();

            // Turn properties containing operation types into relations
            foreach (var relation in SouffleUtils.ExportedProperties.Values)
            {
                var parameters =
                    relation.Parameters
                    .Select(p => $"{p.Name}: {p.Type}");

                writer.WriteLine($".decl {relation.Name}({string.Join(", ", parameters)})");
                writer.WriteLine($".input {relation.Name}");
            }
        }
    }
}
