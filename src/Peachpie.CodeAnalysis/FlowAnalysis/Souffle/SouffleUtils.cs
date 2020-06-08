using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;

namespace Pchp.CodeAnalysis.FlowAnalysis.Souffle
{
    internal static class SouffleUtils
    {
        public static ImmutableHashSet<Type> ExportedTypes { get; }

        public static ImmutableHashSet<Type> ExportedUnionTypes { get; }

        public static ImmutableDictionary<PropertyInfo, SouffleRelation> ExportedProperties { get; }

        static SouffleUtils()
        {
            ExportedTypes =
                typeof(BoundOperation).Assembly.GetTypes()
                .Where(t => IsExportedType(t))
                .ToImmutableHashSet();

            ExportedUnionTypes =
                ExportedTypes
                .Where(t1 => ExportedTypes.Any(t2 => t2.IsSubclassOf(t1)))
                .ToImmutableHashSet();

            var singleProps =
                ExportedTypes
                .SelectMany(t =>
                    t.GetProperties()
                    .Where(p => IsExportedType(p.PropertyType) && !IsInheritedProperty(p, t))
                    .Select(p => new KeyValuePair<PropertyInfo, SouffleRelation>(
                        p,
                        new SouffleRelation(
                            $"{GetOperationTypeName(t)}_{p.Name}",
                            new[] {
                                new SouffleRelation.Parameter("parent", GetOperationTypeName(t)),
                                new SouffleRelation.Parameter("value", GetOperationTypeName(p.PropertyType))
                            }.ToImmutableArray())
                    )));

            var enumerableProps =
                ExportedTypes
                .SelectMany(t =>
                    t.GetProperties()
                    .Where(p =>
                        (typeof(IEnumerable<BoundOperation>).IsAssignableFrom(p.PropertyType) ||
                        typeof(IEnumerable<Edge>).IsAssignableFrom(p.PropertyType)) &&
                        !IsInheritedProperty(p, t))
                    .Select(p => new KeyValuePair<PropertyInfo, SouffleRelation>(
                        p,
                        new SouffleRelation(
                            $"{GetOperationTypeName(t)}_{p.Name}_Item",
                            new[] {
                                new SouffleRelation.Parameter("parent", GetOperationTypeName(t)),
                                new SouffleRelation.Parameter("index", "unsigned"),
                                new SouffleRelation.Parameter("value", GetEnumerablePropertyTypeName(p))
                            }.ToImmutableArray())
                    )));

            ExportedProperties = singleProps.Concat(enumerableProps).ToImmutableDictionary();

            string GetEnumerablePropertyTypeName(PropertyInfo prop)
            {
                var enumerableType =
                    (prop.PropertyType.Name == "IEnumerable`1")
                    ? prop.PropertyType
                    : prop.PropertyType.GetInterface("IEnumerable`1");

                var itemType = enumerableType.GenericTypeArguments[0];

                return GetOperationTypeName(itemType);
            }
        }

        private static bool IsExportedType(Type type) =>
            type.IsSubclassOf(typeof(BoundOperation)) ||
            type.IsSubclassOf(typeof(Edge)) ||
            type == typeof(BoundOperation) ||
            type == typeof(Edge);

        private static bool IsInheritedProperty(PropertyInfo prop, Type type) =>
            prop.DeclaringType != type ||
            prop.GetGetMethod().GetBaseDefinition().DeclaringType != type;

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
