// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Runtime.InteropServices.Marshalling
{
    [AttributeUsage(
        AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue,
        AllowMultiple = true)]
    public sealed class MarshalUsingAttribute : Attribute
    {
        public const string ReturnsCountValue = "return-value";

        public MarshalUsingAttribute()
        {
            CountElementName = string.Empty;
        }

        public MarshalUsingAttribute(Type nativeType)
            : this()
        {
            NativeType = nativeType;
        }

        public Type? NativeType { get; }

        public string CountElementName { get; set; }

        public int ConstantElementCount { get; set; }

        public int ElementIndirectionDepth { get; set; }
    }
}
