// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Runtime.InteropServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Struct, Inherited = false)]
    public sealed class TypeLibTypeAttribute : Attribute
    {
        public TypeLibTypeAttribute(TypeLibTypeFlags flags)
        {
            Value = flags;
        }

        public TypeLibTypeAttribute(short flags)
        {
            Value = (TypeLibTypeFlags)flags;
        }

        public TypeLibTypeFlags Value { get; }
    }
}
