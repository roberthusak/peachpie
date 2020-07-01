using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using Devsense.PHP.Syntax;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;

namespace Pchp.CodeAnalysis.FlowAnalysis.Souffle
{
    internal static class SouffleUtils
    {
        public static SouffleType NodeType { get; }

        public static SouffleType RoutineType { get; }

        public static SouffleType ParameterPassType { get; }

        public static ImmutableHashSet<Type> ExportedTypes { get; }

        public static ImmutableHashSet<Type> ExportedUnionTypes { get; }

        public static ImmutableDictionary<Type, SouffleRelation> ExportedTypeRelations { get; }

        public static ImmutableDictionary<PropertyInfo, SouffleRelation> ExportedProperties { get; }

        public static SouffleRelation RoutineNodeRelation { get; }

        public static SouffleRelation NextRelation { get; }

        private static readonly ImmutableHashSet<(string, string)> ExportedValuePropertyNames =
            new[]
            {
                (nameof(BoundVariableName), nameof(BoundVariableName.NameValue))
            }.ToImmutableHashSet();

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

            ParameterPassType = new SouffleType(
                "ParameterPass",
                ImmutableArray<string>.Empty);

            NodeType = new SouffleType(
                "Node",
                new[]
                {
                    GetTypeName(typeof(BoundOperation)),
                    GetTypeName(typeof(Edge)),
                    ParameterPassType.Name
                }.ToImmutableArray());

            RoutineType = new SouffleType(
                "Routine",
                ImmutableArray<string>.Empty);

            ExportedTypeRelations =
                ExportedTypes
                .Select(t => new KeyValuePair<Type, SouffleRelation>(
                    t,
                    new SouffleRelation(
                        $"Is_{GetTypeName(t, false)}",
                        new[] {
                            new SouffleRelation.Parameter("node", GetTypeName(t)),
                        }.ToImmutableArray())
                    ))
                .ToImmutableDictionary();

            var propertyFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            var singleProps =
                ExportedTypes
                .SelectMany(t =>
                    t.GetProperties(propertyFlags)
                    .Where(p => IsExportedSingleProperty(p) && !IsInheritedProperty(p, t))
                    .Select(p => new KeyValuePair<PropertyInfo, SouffleRelation>(
                        p,
                        new SouffleRelation(
                            $"{GetTypeName(t)}_{p.Name}",
                            new[] {
                                new SouffleRelation.Parameter("parent", GetTypeName(t)),
                                new SouffleRelation.Parameter("value", GetTypeName(p.PropertyType))
                            }.ToImmutableArray())
                    )));

            var enumerableProps =
                ExportedTypes
                .SelectMany(t =>
                    t.GetProperties(propertyFlags)
                    .Where(p =>
                        (typeof(IEnumerable<BoundOperation>).IsAssignableFrom(p.PropertyType) ||
                        typeof(IEnumerable<Edge>).IsAssignableFrom(p.PropertyType)) &&
                        !IsInheritedProperty(p, t))
                    .Select(p => new KeyValuePair<PropertyInfo, SouffleRelation>(
                        p,
                        new SouffleRelation(
                            $"{GetTypeName(t)}_{p.Name}_Item",
                            new[] {
                                new SouffleRelation.Parameter("parent", GetTypeName(t)),
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

                return GetTypeName(itemType);
            }

            RoutineNodeRelation = new SouffleRelation(
                "RoutineNode",
                new[]
                {
                    new SouffleRelation.Parameter("routine", RoutineType.Name),
                    new SouffleRelation.Parameter("node", NodeType.Name)
                }.ToImmutableArray());

            NextRelation = new SouffleRelation(
                "Next",
                new[]
                {
                    new SouffleRelation.Parameter("from", NodeType.Name),
                    new SouffleRelation.Parameter("to", NodeType.Name)
                }.ToImmutableArray());
        }

        private static bool IsExportedType(Type type) =>
            type.IsSubclassOf(typeof(BoundOperation)) ||
            type.IsSubclassOf(typeof(Edge)) ||
            type == typeof(BoundOperation) ||
            type == typeof(Edge);

        private static bool IsExportedSingleProperty(PropertyInfo prop) =>
            IsExportedType(prop.PropertyType) || ExportedValuePropertyNames.Contains((prop.DeclaringType.Name, prop.Name));

        private static bool IsInheritedProperty(PropertyInfo prop, Type type) =>
            prop.DeclaringType != type ||
            prop.GetMethod.GetBaseDefinition().DeclaringType != type;

        public static bool IsUnionType(Type type) => ExportedUnionTypes.Contains(type);

        public static string GetTypeName(Type type) =>
            GetTypeName(type, IsUnionType(type));

        public static string GetTypeName(Type type, bool isBase)
        {
            if (type == typeof(string) || type == typeof(VariableName))
            {
                return SouffleType.SymbolTypeName;
            }

            string name = type.Name;

            // Strip the Bound- prefix
            name = RemovePrefix(name, "Bound");

            // Unify by skipping possible suffixes present
            name = RemoveSuffix(name, "Ex");
            name = RemoveSuffix(name, "Expression");
            name = RemoveSuffix(name, "Statement");

            // Distinguish Souffle type from union in non-abstract types by -Base suffix
            if (isBase && !type.IsAbstract)
            {
                name += "Base";
            }

            // Mark expressions and statements by short suffixes
            if (type.IsSubclassOf(typeof(BoundExpression)))
            {
                name += "Ex";
            }
            else if (type.IsSubclassOf(typeof(BoundStatement)) &&
                !type.IsSubclassOf(typeof(BoundBlock)) && type != typeof(BoundBlock))
            {
                name += "St";
            }

            return name;
        }

        private static string RemovePrefix(string subject, string prefix)
        {
            if (subject.StartsWith(prefix) && subject != prefix)
            {
                return subject.Substring(prefix.Length);
            }
            else
            {
                return subject;
            }
        }

        private static string RemoveSuffix(string subject, string suffix)
        {
            if (subject.EndsWith(suffix) && subject != suffix)
            {
                return subject.Substring(0, subject.Length - suffix.Length);
            }
            else
            {
                return subject;
            }
        }
    }
}
