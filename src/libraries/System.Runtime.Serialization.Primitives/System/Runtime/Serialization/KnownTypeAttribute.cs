// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Runtime.Serialization
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true, AllowMultiple = true)]
    public sealed class KnownTypeAttribute : Attribute
    {
        public KnownTypeAttribute(Type type)
        {
            Type = type;
        }

        public KnownTypeAttribute(string methodName)
        {
            MethodName = methodName;
        }

        public string? MethodName { get; }

        public Type? Type { get; }
    }
}
