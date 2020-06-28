using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Pchp.CodeAnalysis.Symbols;
using Ast = Devsense.PHP.Syntax.Ast;

namespace Pchp.CodeAnalysis.Semantics
{
    public abstract class BoundOperation : IOperation
    {
        #region Unsupported

        SyntaxNode IOperation.Syntax => null;

        IOperation IOperation.Parent => null;

        IEnumerable<IOperation> IOperation.Children => Array.Empty<IOperation>();

        SemanticModel IOperation.SemanticModel => null;

        #endregion

        #region Souffle

        private static int NextSerial;

        internal static int GetFreeSerialNumber() => Interlocked.Increment(ref NextSerial);

        internal int SerialNumber { get; }

        #endregion

        public BoundOperation()
        {
            SerialNumber = GetFreeSerialNumber();
        }

        public string Language => Constants.PhpLanguageName;

        public virtual bool IsImplicit => false;

        public abstract OperationKind Kind { get; }

        public virtual ITypeSymbol Type => null;

        /// <summary>
        /// Resolved value of the expression.
        /// </summary>
        Optional<object> IOperation.ConstantValue => ConstantValueHlp;

        protected virtual Optional<object> ConstantValueHlp => default(Optional<object>);

        public abstract void Accept(OperationVisitor visitor);

        public abstract TResult Accept<TArgument, TResult>(OperationVisitor<TArgument, TResult> visitor, TArgument argument);
    }
}
