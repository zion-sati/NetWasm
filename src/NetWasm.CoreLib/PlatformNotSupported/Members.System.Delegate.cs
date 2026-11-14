// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce reference sources.
// NetWasm classifies these APIs as intentionally unsupported: they bind, and every member throws
// PlatformNotSupportedException.

#pragma warning disable CS0067, CS0169, CS0618, CS0809, CS8597, CS8618, CS8625, SYSLIB0050, SYSLIB0051

namespace System
{
    partial class Delegate
    {
        protected virtual System.Reflection.MethodInfo GetMethodImpl() { throw new global::System.PlatformNotSupportedException("Delegate.GetMethodImpl reflection-based member access is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("The target method might be removed")]
        protected Delegate(object target, string method) { throw new global::System.PlatformNotSupportedException("Delegate reflection-based member access is not available on this NetWasm platform."); }
        protected Delegate([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.AllMethods)] System.Type target, string method) { throw new global::System.PlatformNotSupportedException("Delegate reflection-based member access is not available on this NetWasm platform."); }
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
        [System.ObsoleteAttribute("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        public virtual void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) { throw new global::System.PlatformNotSupportedException("Delegate.GetObjectData formatter serialization is not available on this NetWasm platform."); }
        public static System.Delegate CreateDelegate(System.Type type, object? firstArgument, System.Reflection.MethodInfo method) { throw new global::System.PlatformNotSupportedException("Delegate.CreateDelegate reflection-based member access is not available on this NetWasm platform."); }
        public static System.Delegate? CreateDelegate(System.Type type, object? firstArgument, System.Reflection.MethodInfo method, bool throwOnBindFailure) { throw new global::System.PlatformNotSupportedException("Delegate.CreateDelegate reflection-based member access is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("The target method might be removed")]
        public static System.Delegate CreateDelegate(System.Type type, object target, string method) { throw new global::System.PlatformNotSupportedException("Delegate.CreateDelegate reflection-based member access is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("The target method might be removed")]
        public static System.Delegate CreateDelegate(System.Type type, object target, string method, bool ignoreCase) { throw new global::System.PlatformNotSupportedException("Delegate.CreateDelegate reflection-based member access is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("The target method might be removed")]
        public static System.Delegate? CreateDelegate(System.Type type, object target, string method, bool ignoreCase, bool throwOnBindFailure) { throw new global::System.PlatformNotSupportedException("Delegate.CreateDelegate reflection-based member access is not available on this NetWasm platform."); }
        public static System.Delegate CreateDelegate(System.Type type, System.Reflection.MethodInfo method) { throw new global::System.PlatformNotSupportedException("Delegate.CreateDelegate reflection-based member access is not available on this NetWasm platform."); }
        public static System.Delegate? CreateDelegate(System.Type type, System.Reflection.MethodInfo method, bool throwOnBindFailure) { throw new global::System.PlatformNotSupportedException("Delegate.CreateDelegate reflection-based member access is not available on this NetWasm platform."); }
        public static System.Delegate CreateDelegate(System.Type type, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.AllMethods)] System.Type target, string method) { throw new global::System.PlatformNotSupportedException("Delegate.CreateDelegate reflection-based member access is not available on this NetWasm platform."); }
        public static System.Delegate CreateDelegate(System.Type type, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.AllMethods)] System.Type target, string method, bool ignoreCase) { throw new global::System.PlatformNotSupportedException("Delegate.CreateDelegate reflection-based member access is not available on this NetWasm platform."); }
        public static System.Delegate? CreateDelegate(System.Type type, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.AllMethods)] System.Type target, string method, bool ignoreCase, bool throwOnBindFailure) { throw new global::System.PlatformNotSupportedException("Delegate.CreateDelegate reflection-based member access is not available on this NetWasm platform."); }
        public System.Reflection.MethodInfo Method { get { throw new global::System.PlatformNotSupportedException("Delegate.Method reflection-based member access is not available on this NetWasm platform."); } }
    }
}
