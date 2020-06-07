using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using Pchp.CodeAnalysis.Semantics;
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
            var exprTypes =
                typeof(BoundExpression).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(BoundExpression)))
                .Append(typeof(BoundExpression))
                .ToHashSet();

            var leafExprTypes =
                exprTypes
                .Where(t1 => !exprTypes.Any(t2 => t2.IsSubclassOf(t1)))
                .ToHashSet();

            var unionExprTypes = exprTypes.Except(leafExprTypes);

            // Base types
            foreach (var exprType in exprTypes.Where(t => !t.IsAbstract))
            {
                string name = SouffleUtils.GetExpressionTypeName(exprType, false);
                writer.WriteLine($".type {name} <: symbol");
            }

            writer.WriteLine();

            // Turn inheritance into union hierarchy
            foreach (var exprType in unionExprTypes)
            {
                var subTypes =
                    exprTypes
                    .Where(t => t.BaseType == exprType)
                    .Select(t => SouffleUtils.GetExpressionTypeName(t, unionExprTypes.Contains(t)))
                    .ToArray();

                if (!exprType.IsAbstract)
                {
                    subTypes = subTypes.Prepend(SouffleUtils.GetExpressionTypeName(exprType, false)).ToArray();
                }

                string name = SouffleUtils.GetExpressionTypeName(exprType, true);
                writer.WriteLine($".type {name} = " + string.Join(" | ", (object[])subTypes));
            }

            writer.WriteLine();

            // Turn properties containing expression types into relations
            foreach (var parentType in exprTypes)
            {
                var props =
                    parentType.GetProperties()
                    .Where(p =>
                        (p.PropertyType == typeof(BoundExpression) || p.PropertyType.IsSubclassOf(typeof(BoundExpression))) &&
                        p.DeclaringType == parentType && p.GetGetMethod().GetBaseDefinition().DeclaringType == parentType)
                    .ToArray();

                foreach (var prop in props)
                {
                    string parentTypeName = SouffleUtils.GetExpressionTypeName(parentType, unionExprTypes.Contains(parentType));
                    string propTypeName = SouffleUtils.GetExpressionTypeName(prop.PropertyType, unionExprTypes.Contains(parentType));
                    writer.WriteLine($".decl {parentTypeName}_{prop.Name}(parent: {parentTypeName}, value: {propTypeName})");
                }
            }
        }
    }
}
