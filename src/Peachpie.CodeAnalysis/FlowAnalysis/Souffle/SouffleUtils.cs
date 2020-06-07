using System;
using System.Collections.Generic;
using System.Text;
using Pchp.CodeAnalysis.Semantics;

namespace Peachpie.CodeAnalysis.FlowAnalysis.Souffle
{
    internal static class SouffleUtils
    {
        public static string GetExpressionTypeName(Type exprType, bool isBase)
        {
            if (exprType == typeof(BoundExpression))
            {
                return "Expression";
            }

            string name = exprType.Name;

            if (name.StartsWith("Bound"))
            {
                name = name.Substring("Bound".Length);
            }

            if (name.EndsWith("Ex"))
            {
                name = name.Substring(0, name.Length - "Ex".Length);
            }

            if (name.EndsWith("Expression"))
            {
                name = name.Substring(0, name.Length - "Expression".Length);
            }

            if (isBase)
            {
                name += "Base";
            }

            return name + "Ex";
        }
    }
}
