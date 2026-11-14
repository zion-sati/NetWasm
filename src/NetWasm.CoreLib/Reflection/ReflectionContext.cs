// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Reflection
{
    public abstract class ReflectionContext
    {
        protected ReflectionContext() { }

        public abstract Assembly MapAssembly(Assembly assembly);

        public abstract TypeInfo MapType(TypeInfo type);

        public virtual TypeInfo GetTypeForObject(object value)
        {
            ArgumentNullException.ThrowIfNull(value);

            return MapType(value.GetType().GetTypeInfo());
        }
    }
}
