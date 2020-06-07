using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices.ComTypes;
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
            var opTypes =
                typeof(BoundOperation).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(BoundOperation)) || t.IsSubclassOf(typeof(Edge)))
                .Append(typeof(BoundOperation))
                .Append(typeof(Edge))
                .ToHashSet();

            var leafOpTypes =
                opTypes
                .Where(t1 => !opTypes.Any(t2 => t2.IsSubclassOf(t1)))
                .ToHashSet();

            var unionOpTypes = opTypes.Except(leafOpTypes);

            // Base types
            foreach (var opType in opTypes.Where(t => !t.IsAbstract))
            {
                string name = SouffleUtils.GetOperationTypeName(opType, false);
                writer.WriteLine($".type {name} <: symbol");
            }

            writer.WriteLine();

            // Turn inheritance into union hierarchy
            foreach (var opType in unionOpTypes)
            {
                var subTypes =
                    opTypes
                    .Where(t => t.BaseType == opType)
                    .Select(t => SouffleUtils.GetOperationTypeName(t, unionOpTypes.Contains(t)))
                    .ToArray();

                if (!opType.IsAbstract)
                {
                    subTypes = subTypes.Prepend(SouffleUtils.GetOperationTypeName(opType, false)).ToArray();
                }

                string name = SouffleUtils.GetOperationTypeName(opType, true);
                writer.WriteLine($".type {name} = " + string.Join(" | ", (object[])subTypes));
            }

            writer.WriteLine();

            // Turn properties containing operation types into relations
            foreach (var parentType in opTypes)
            {
                string parentTypeName = SouffleUtils.GetOperationTypeName(parentType, unionOpTypes.Contains(parentType));

                var singleProps =
                    parentType.GetProperties()
                    .Where(p =>
                        (p.PropertyType == typeof(BoundOperation) || p.PropertyType.IsSubclassOf(typeof(BoundOperation)) ||
                        p.PropertyType == typeof(Edge) || p.PropertyType.IsSubclassOf(typeof(Edge))) &&
                        p.DeclaringType == parentType && p.GetGetMethod().GetBaseDefinition().DeclaringType == parentType)
                    .ToArray();

                foreach (var prop in singleProps)
                {
                    string propTypeName = SouffleUtils.GetOperationTypeName(prop.PropertyType, unionOpTypes.Contains(prop.PropertyType));
                    writer.WriteLine($".decl {parentTypeName}_{prop.Name}(parent: {parentTypeName}, value: {propTypeName})");
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
                    string propTypeName = SouffleUtils.GetOperationTypeName(itemType, unionOpTypes.Contains(itemType));

                    writer.WriteLine($".decl {parentTypeName}_{prop.Name}_Item(parent: {parentTypeName}, index: unsigned, value: {propTypeName})");
                }
            }
        }
    }
}
