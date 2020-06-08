using System;
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
            foreach (var parentType in SouffleUtils.ExportedTypes)
            {
                string parentTypeName = SouffleUtils.GetOperationTypeName(parentType);

                var singleProps =
                    parentType.GetProperties()
                    .Where(p =>
                        (p.PropertyType == typeof(BoundOperation) || p.PropertyType.IsSubclassOf(typeof(BoundOperation)) ||
                        p.PropertyType == typeof(Edge) || p.PropertyType.IsSubclassOf(typeof(Edge))) &&
                        p.DeclaringType == parentType && p.GetGetMethod().GetBaseDefinition().DeclaringType == parentType)
                    .ToArray();

                foreach (var prop in singleProps)
                {
                    string propTypeName = SouffleUtils.GetOperationTypeName(prop.PropertyType);
                    writer.WriteLine($".decl {parentTypeName}_{prop.Name}(parent: {parentTypeName}, value: {propTypeName})");
                    writer.WriteLine($".input {parentTypeName}_{prop.Name}");
                }

                var enumerableProps =
                    parentType.GetProperties()
                    .Where(p =>
                        typeof(IEnumerable<BoundOperation>).IsAssignableFrom(p.PropertyType) &&                             // There are no Edge enumerables, no need to handle them
                        p.DeclaringType == parentType && p.GetGetMethod().GetBaseDefinition().DeclaringType == parentType)
                    .ToArray();

                foreach (var prop in enumerableProps)
                {
                    var enumerableType = (prop.PropertyType.Name == "IEnumerable`1") ? prop.PropertyType : prop.PropertyType.GetInterface("IEnumerable`1");
                    var itemType = enumerableType.GenericTypeArguments[0];
                    string propTypeName = SouffleUtils.GetOperationTypeName(itemType);

                    writer.WriteLine($".decl {parentTypeName}_{prop.Name}_Item(parent: {parentTypeName}, index: unsigned, value: {propTypeName})");
                    writer.WriteLine($".input {parentTypeName}_{prop.Name}_Item");
                }
            }
        }
    }
}
