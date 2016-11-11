﻿using Devsense.PHP.Syntax;
using Microsoft.CodeAnalysis;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Symbols;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pchp.CodeAnalysis.FlowAnalysis
{
    /// <summary>
    /// Manages context of local variables, their merged type and return value type.
    /// </summary>
    public class FlowContext
    {
        #region Constants

        /// <summary>
        /// Size of ulong bit array (<c>64</c>).
        /// </summary>
        internal const int BitsCount = sizeof(ulong) * 8;

        #endregion

        #region Fields & Properties

        /// <summary>
        /// Associated type context.
        /// </summary>
        public TypeRefContext TypeRefContext => _typeCtx;
        readonly TypeRefContext/*!*/_typeCtx;

        /// <summary>
        /// Reference to corresponding routine symbol. Can be a <c>null</c> reference.
        /// </summary>
        internal SourceRoutineSymbol Routine => _routine;
        readonly SourceRoutineSymbol _routine;

        /// <summary>
        /// Map of variables name and their index.
        /// </summary>
        readonly Dictionary<VariableName, int>/*!*/_varsIndex;

        /// <summary>
        /// Bit mask of variables where bit with value <c>1</c> signalizes that variables with index corresponding to the bit number has been used.
        /// </summary>
        ulong _usedMask;

        /// <summary>
        /// Merged local variables type.
        /// </summary>
        internal TypeRefMask[] VarsType => _varsType;
        TypeRefMask[]/*!*/_varsType = EmptyArray<TypeRefMask>.Instance;

        /// <summary>
        /// Merged return expressions type.
        /// </summary>
        internal TypeRefMask ReturnType { get { return _returnType; } set { _returnType = value; } }
        TypeRefMask _returnType;

        #endregion

        #region Construction

        internal FlowContext(TypeRefContext/*!*/typeCtx, SourceRoutineSymbol routine)
        {
            Contract.ThrowIfNull(typeCtx);
            
            _typeCtx = typeCtx;
            _routine = routine;

            //
            _varsIndex = new Dictionary<VariableName, int>();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets index of variable within the context.
        /// </summary>
        public int GetVarIndex(VariableName name)
        {
            Debug.Assert(!name.IsEmpty());

            int index;
            if (!_varsIndex.TryGetValue(name, out index))
            {
                lock (_varsType)
                {
                    index = _varsType.Length;
                    Array.Resize(ref _varsType, index + 1);
                }

                _varsIndex[name] = index;
            }

            return index;
        }

        public void SetReference(int varindex)
        {
            if (varindex >= 0 && varindex < BitsCount)
            {
                _varsType[varindex] |= TypeRefMask.IsRefMask;
            }
        }

        /// <summary>
        /// Gets value indicating whether given variable might be a reference.
        /// </summary>
        public bool IsReference(int varindex)
        {
            // anything >= 64 is reported as a possible reference
            return varindex < 0 || varindex >= _varsType.Length || _varsType[varindex].IsRef;
        }

        public void AddVarType(int varindex, TypeRefMask type)
        {
            if (varindex >= 0 && varindex < _varsType.Length)
            {
                _varsType[varindex] |= type;
            }
        }

        public TypeRefMask GetVarType(VariableName name)
        {
            return _varsType[GetVarIndex(name)];
        }

        /// <summary>
        /// Sets specified variable as being used.
        /// </summary>
        public void SetUsed(int varindex)
        {
            if (varindex >= 0 && varindex < BitsCount)
            {
                _usedMask |= (ulong)1 << varindex;
            }
        }

        /// <summary>
        /// Marks all local variables as used.
        /// </summary>
        public void SetAllUsed()
        {
            _usedMask = ~(ulong)0;
        }

        public bool IsUsed(int varindex)
        {
            // anything >= 64 is used
            return varindex < 0 || varindex >= BitsCount || (_usedMask & (ulong)1 << varindex) != 0;
        }

        #endregion
    }
}
