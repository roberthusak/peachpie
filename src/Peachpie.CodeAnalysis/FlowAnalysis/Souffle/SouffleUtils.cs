using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;

namespace Peachpie.CodeAnalysis.FlowAnalysis.Souffle
{
    internal static class SouffleUtils
    {
        public static ImmutableHashSet<Type> ExportedTypes { get; }

        public static ImmutableHashSet<Type> ExportedUnionTypes { get; }

        static SouffleUtils()
        {
            ExportedTypes =
                typeof(BoundOperation).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(BoundOperation)) || t.IsSubclassOf(typeof(Edge)))
                .Append(typeof(BoundOperation))
                .Append(typeof(Edge))
                .ToImmutableHashSet();

            ExportedUnionTypes =
                ExportedTypes
                .Where(t1 => ExportedTypes.Any(t2 => t2.IsSubclassOf(t1)))
                .ToImmutableHashSet();
        }

        public static bool IsUnionType(Type type) => ExportedUnionTypes.Contains(type);

        public static string GetOperationTypeName(Type exprType) =>
            GetOperationTypeName(exprType, IsUnionType(exprType));

        public static string GetOperationTypeName(Type exprType, bool isBase)
        {
            string name = exprType.Name;

            // Strip the Bound- prefix
            if (name.StartsWith("Bound"))
            {
                name = name.Substring("Bound".Length);
            }

            // Unify by skipping the -Ex suffix if present
            if (name.EndsWith("Ex"))
            {
                name = name.Substring(0, name.Length - "Ex".Length);
            }

            // The same with possible -Expression suffix as well
            if (name.EndsWith("Expression") && name != "Expression")
            {
                name = name.Substring(0, name.Length - "Expression".Length);
            }

            // Distinguish Souffle type from union in non-abstract types by -Base suffix
            if (isBase && !exprType.IsAbstract)
            {
                name += "Base";
            }

            // Mark expressions by -Ex suffix
            if (exprType.IsSubclassOf(typeof(BoundExpression)))
            {
                name += "Ex";
            }

            return name;
        }
    }
}
