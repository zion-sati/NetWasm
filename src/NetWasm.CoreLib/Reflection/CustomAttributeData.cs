// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace System.Reflection
{
    public class CustomAttributeData
    {
        #region Public Static Members
        public static IList<CustomAttributeData> GetCustomAttributes(MemberInfo target)
        {
            ArgumentNullException.ThrowIfNull(target);

            return target.GetCustomAttributesData();
        }

        public static IList<CustomAttributeData> GetCustomAttributes(Module target)
        {
            ArgumentNullException.ThrowIfNull(target);

            return target.GetCustomAttributesData();
        }

        public static IList<CustomAttributeData> GetCustomAttributes(Assembly target)
        {
            ArgumentNullException.ThrowIfNull(target);

            return target.GetCustomAttributesData();
        }

        public static IList<CustomAttributeData> GetCustomAttributes(ParameterInfo target)
        {
            ArgumentNullException.ThrowIfNull(target);

            return target.GetCustomAttributesData();
        }
        #endregion

        protected CustomAttributeData()
        {
        }

        #region Object Override
        // Retain the explicit .NET Standard declarations; current upstream inherits these.
        public override bool Equals(object? obj) => base.Equals(obj);
        public override int GetHashCode() => base.GetHashCode();

        public override unsafe string ToString()
        {
            var vsb = new ValueStringBuilder(stackalloc char[256]);

            vsb.Append('[');
            vsb.Append(Constructor.DeclaringType!.FullName);
            vsb.Append('(');

            var first = true;

            var constructorArguments = ConstructorArguments;
            var constructorArgumentsCount = constructorArguments.Count;
            for (var i = 0; i < constructorArgumentsCount; i++)
            {
                if (!first) vsb.Append(", ");
                vsb.Append(constructorArguments[i].ToString());
                first = false;
            }

            var namedArguments = NamedArguments;
            var namedArgumentsCount = namedArguments.Count;
            for (var i = 0; i < namedArgumentsCount; i++)
            {
                if (!first) vsb.Append(", ");
                vsb.Append(namedArguments[i].ToString());
                first = false;
            }

            vsb.Append(")]");

            return vsb.ToString();
        }
        #endregion

        #region Public Members
        public virtual Type AttributeType => Constructor.DeclaringType!;

        // Expected to be overridden
        public virtual ConstructorInfo Constructor => null!;
        public virtual IList<CustomAttributeTypedArgument> ConstructorArguments => null!;
        public virtual IList<CustomAttributeNamedArgument> NamedArguments => null!;
        #endregion
    }
}
