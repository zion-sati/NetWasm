// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit 474ed963699fc29809015392738a15231aaae2de.

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
