﻿using Pchp.Core;
using Pchp.Core.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using System.Diagnostics;

namespace Pchp.Library.Reflection
{
    [PhpType(PhpTypeAttribute.InheritName), PhpExtension(ReflectionUtils.ExtensionName)]
    public abstract class ReflectionFunctionAbstract : Reflector
    {
        #region Fields & Properties

        /// <summary>
        /// Gets name of the function.
        /// </summary>
        public string name
        {
            get
            {
                return _routine.Name;
            }
            //set
            //{
            //    // Read-only, throws ReflectionException in attempt to write.
            //    throw new ReflectionException(); // TODO: message
            //}
        }

        /// <summary>
        /// Underlaying routine information.
        /// Cannot be <c>null</c>.
        /// </summary>
        private protected RoutineInfo _routine;

        #endregion

        /// <summary>Helper that gets the routine's return type.</summary>
        private protected bool ResolveReturnType(out Type type, out bool notNullFlag)
        {
            var m = _routine.Methods[0];

            type = m.ReturnType;
            notNullFlag =
                m.ReturnTypeCustomAttributes.IsDefined(typeof(NotNullAttribute), false) ||
                m.ReturnTypeCustomAttributes.IsDefined(typeof(CastToFalse), false); // [return: CastToFalse] => NULL cannot be returned

            //
            return type != typeof(PhpValue) && type != typeof(PhpAlias); // dunno
        }

        //private void __clone(void ) { throw new NotImplementedException(); }
        public virtual ReflectionClass getClosureScopeClass() { throw new NotImplementedException(); }
        public virtual object getClosureThis() { throw new NotImplementedException(); }
        [return: CastToFalse]
        public string getDocComment() => ReflectionUtils.getDocComment(_routine.Methods[0]);

        public virtual long getStartLine()
        {
            PhpException.FunctionNotSupported("ReflectionFunctionAbstract::getStartLine");
            return 0;
        }

        public virtual long getEndLine()
        {
            PhpException.FunctionNotSupported("ReflectionFunctionAbstract::getEndLine");
            return 0;
        }

        public ReflectionExtension getExtension()
        {
            var extname = getExtensionName();
            return extname != null ? new ReflectionExtension(extname) : null;
        }
        [return: CastToFalse]
        public string getExtensionName()
        {
            var containingType = _routine.Methods[0].DeclaringType;
            var extensionName = containingType.GetCustomAttribute<PhpExtensionAttribute>(false)?.FirstExtensionOrDefault;

            return extensionName;
        }
        public virtual string getFileName(Context ctx) { throw new NotImplementedException(); }
        public string getName() => name;
        public string getNamespaceName()
        {
            // opposite of getShortName()
            var name = this.name;
            var sep = name.LastIndexOf(ReflectionUtils.NameSeparator);
            return (sep < 0) ? string.Empty : name.Remove(sep);
        }
        
        public long getNumberOfParameters()
        {
            var parameters = ReflectionUtils.ResolvePhpParameters(_routine.Methods);
            int count = 0;

            for (; count < parameters.Count; count++)
            {
                var p = parameters[count];
                
                if (p.GetCustomAttribute<ParamArrayAttribute>() != null)
                {
                    // variadic is the last one
                    return count + 1;
                }
            }

            return count;
        }

        public long getNumberOfRequiredParameters()
        {
            var parameters = ReflectionUtils.ResolvePhpParameters(_routine.Methods);
            int count = 0;

            for (; count < parameters.Count; count++)
            {
                var p = parameters[count];
                if (p.HasDefaultValue || p.GetCustomAttribute<DefaultValueAttribute>() != null) // is optional argument
                    break;

                if (p.GetCustomAttribute<ParamArrayAttribute>() != null) // is optional, variadic
                    break;
            }

            return count;
        }

        /// <summary>
        /// Get the parameters as an array of <see cref="ReflectionParameter"/>.
        /// </summary>
        /// <returns>The parameters, as <see cref="ReflectionParameter"/> objects.</returns>
        public PhpArray getParameters(Context ctx)
        {
            var parameters = ReflectionUtils.ResolveReflectionParameters(ctx, this, _routine.Methods);

            //

            var arr = new PhpArray(parameters.Count);
            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];

                arr.Add(PhpValue.FromClass(p));

                if (p.isVariadic()) break; // variadic must be the last one
            }

            return arr;
        }
        public virtual ReflectionType getReturnType()
        {
            return ResolveReturnType(out var t, out var notNullFlag) ? new ReflectionNamedType(t, notNullFlag) : null;
        }
        public string getShortName()
        {
            var name = this.name;
            var sep = name.LastIndexOf(ReflectionUtils.NameSeparator);
            return (sep < 0) ? name : name.Substring(sep + 1);
        }

        [return: NotNull]
        public PhpArray getStaticVariables(Context ctx)
        {
            var arr = new PhpArray();

            var locals = _routine.GetStaticLocals(ctx);
            foreach (var local in locals)
            {
                arr[local.Key] = (local.Value != null) ? local.Value.DeepCopy() : PhpValue.Null;
            }

            return arr;
        }
        public virtual bool hasReturnType()
        {
            return ResolveReturnType(out var _, out var _);
        }
        public bool inNamespace() => name.IndexOf(ReflectionUtils.NameSeparator) > 0;
        public virtual bool isClosure() { throw new NotImplementedException(); }
        public virtual bool isDeprecated() { throw new NotImplementedException(); }
        public bool isGenerator() { throw new NotImplementedException(); }
        public bool isInternal() => !isUserDefined(); // CONSIDER: rather check MethodInfo Assembly PublicKeyToken
        public bool isUserDefined() => _routine.IsUserFunction;
        public bool isVariadic() => _routine.Methods.Any(m => m.GetParameters().Any(p => p.GetCustomAttribute<ParamArrayAttribute>() != null));
        public bool returnsReference() => _routine.Methods.Any(m => m.ReturnType == typeof(PhpAlias));

        public virtual string __toString() { throw new NotImplementedException(); }

        [PhpHidden]
        public override string ToString() => __toString();
    }
}
