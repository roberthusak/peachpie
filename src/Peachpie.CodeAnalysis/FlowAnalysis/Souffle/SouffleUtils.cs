using System;
using System.Collections.Generic;
using System.Text;
using Pchp.CodeAnalysis.Semantics;

namespace Peachpie.CodeAnalysis.FlowAnalysis.Souffle
{
    internal static class SouffleUtils
    {
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
