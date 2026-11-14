// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Runtime.InteropServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class ManagedToNativeComInteropStubAttribute : Attribute
    {
        public ManagedToNativeComInteropStubAttribute(Type classType, string methodName)
        {
            ClassType = classType;
            MethodName = methodName;
        }

        public Type ClassType { get; }
        public string MethodName { get; }
    }
}
