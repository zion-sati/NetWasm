// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Runtime.InteropServices.Marshalling
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = true)]
    public sealed class CustomMarshallerAttribute : Attribute
    {
        public CustomMarshallerAttribute(Type managedType, MarshalMode marshalMode, Type marshallerType)
        {
            ManagedType = managedType;
            MarshalMode = marshalMode;
            MarshallerType = marshallerType;
        }

        public Type ManagedType { get; }

        public MarshalMode MarshalMode { get; }

        public Type MarshallerType { get; }

        public struct GenericPlaceholder
        {
        }
    }
}
