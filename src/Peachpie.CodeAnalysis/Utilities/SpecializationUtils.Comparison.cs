using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Pchp.CodeAnalysis.Symbols;

namespace Peachpie.CodeAnalysis.Utilities
{
    internal static partial class SpecializationUtils
    {
        public static IComparer<TypeSymbol> ParameterTypeComparer { get; } = new ParameterTypeComparerImpl();

        public static IComparer<ImmutableArray<TypeSymbol>> SpecializationComparer { get; } = new SpecializationComparerImpl();

        private class ParameterTypeComparerImpl : IComparer<TypeSymbol>
        {
            public int Compare(TypeSymbol x, TypeSymbol y)
            {
                if (x.Equals(y))
                    return 0;

                int xPriority = GetTypePriority(x);
                int yPriority = GetTypePriority(y);

                if (xPriority != yPriority)
                    return xPriority - yPriority;

                int nameDiff = string.CompareOrdinal(x.Name, y.Name);

                if (nameDiff == 0)
                    throw new NotImplementedException($"Comparison of different types with the same name '{x.Name}'");

                return nameDiff;
            }

            private static int GetTypePriority(TypeSymbol type)
            {
                switch (type.SpecialType)
                {
                    case SpecialType.System_Boolean:
                    case SpecialType.System_Char:
                    case SpecialType.System_SByte:
                    case SpecialType.System_Byte:
                    case SpecialType.System_Int16:
                    case SpecialType.System_UInt16:
                    case SpecialType.System_Int32:
                    case SpecialType.System_UInt32:
                        return 1;

                    case SpecialType.System_Int64:
                    case SpecialType.System_UInt64:
                        return 2;

                    case SpecialType.System_Single:
                    case SpecialType.System_Double:
                        return 3;

                    case SpecialType.System_String:
                        return 6;

                    case SpecialType.System_Object:
                        return 7;
                }

                if (type.Is_PhpNumber())
                    return 4;
                if (type.Is_PhpString())
                    return 9;
                if (type.Is_PhpArray())
                    return 10;
                if (type.Is_PhpValue())
                    return 11;

                Debug.Assert(!type.Is_PhpAlias());  // Parameters passed by reference are not currently specialized

                if (type.IsValueType)
                    return 5;
                if (type.IsReferenceType)
                    return 8;

                throw new NotImplementedException($"Comparison of type '{type}'");
            }
        }

        private class SpecializationComparerImpl : IComparer<ImmutableArray<TypeSymbol>>
        {
            public int Compare(ImmutableArray<TypeSymbol> x, ImmutableArray<TypeSymbol> y)
            {
                // Lexicographic comparison
                Debug.Assert(x.Length == y.Length);
                for (int i = 0; i < x.Length; i++)
                {
                    int result = ParameterTypeComparer.Compare(x[i], y[i]);
                    if (result != 0)
                    {
                        return result;
                    }
                }
                return 0;
            }
        }
    }
}
