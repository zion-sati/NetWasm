// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce reference sources.
// NetWasm classifies these APIs as intentionally unsupported: they bind, and every member throws
// PlatformNotSupportedException.

#pragma warning disable CS0067, CS0169, CS0618, CS0809, CS8597, CS8618, CS8625, SYSLIB0050, SYSLIB0051

namespace System.Reflection.Emit
{
    public partial class DynamicILInfo
    {
        internal DynamicILInfo() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.DynamicMethod DynamicMethod { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public int GetTokenFor(byte[] signature) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public int GetTokenFor(System.Reflection.Emit.DynamicMethod method) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public int GetTokenFor(System.RuntimeFieldHandle field) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public int GetTokenFor(System.RuntimeFieldHandle field, System.RuntimeTypeHandle contextType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public int GetTokenFor(System.RuntimeMethodHandle method) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public int GetTokenFor(System.RuntimeMethodHandle method, System.RuntimeTypeHandle contextType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public int GetTokenFor(System.RuntimeTypeHandle type) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public int GetTokenFor(string literal) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.CLSCompliantAttribute(false)]
        public unsafe void SetCode(byte* code, int codeSize, int maxStackSize) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCode(byte[]? code, int maxStackSize) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.CLSCompliantAttribute(false)]
        public unsafe void SetExceptions(byte* exceptions, int exceptionsSize) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetExceptions(byte[]? exceptions) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.CLSCompliantAttribute(false)]
        public unsafe void SetLocalSignature(byte* localSignature, int signatureSize) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetLocalSignature(byte[]? localSignature) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
    }

    public sealed partial class DynamicMethod : System.Reflection.MethodInfo
    {
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Creating a DynamicMethod requires dynamic code.")]
        public DynamicMethod(string name, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes, System.Reflection.Module m, bool skipVisibility) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Creating a DynamicMethod requires dynamic code.")]
        public DynamicMethod(string name, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes, System.Type owner, bool skipVisibility) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Creating a DynamicMethod requires dynamic code.")]
        public DynamicMethod(string name, System.Type? returnType, System.Type[]? parameterTypes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Creating a DynamicMethod requires dynamic code.")]
        public DynamicMethod(string name, System.Type? returnType, System.Type[]? parameterTypes, bool restrictedSkipVisibility) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Creating a DynamicMethod requires dynamic code.")]
        public DynamicMethod(string name, System.Type? returnType, System.Type[]? parameterTypes, System.Reflection.Module m) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Creating a DynamicMethod requires dynamic code.")]
        public DynamicMethod(string name, System.Type? returnType, System.Type[]? parameterTypes, System.Reflection.Module m, bool skipVisibility) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Creating a DynamicMethod requires dynamic code.")]
        public DynamicMethod(string name, System.Type? returnType, System.Type[]? parameterTypes, System.Type owner) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Creating a DynamicMethod requires dynamic code.")]
        public DynamicMethod(string name, System.Type? returnType, System.Type[]? parameterTypes, System.Type owner, bool skipVisibility) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.MethodAttributes Attributes { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.CallingConventions CallingConvention { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? DeclaringType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public bool InitLocals { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } set { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsSecurityCritical { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsSecuritySafeCritical { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsSecurityTransparent { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.RuntimeMethodHandle MethodHandle { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.Module Module { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string Name { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? ReflectedType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.ParameterInfo ReturnParameter { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.ICustomAttributeProvider ReturnTypeCustomAttributes { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public sealed override System.Delegate CreateDelegate(System.Type delegateType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public sealed override System.Delegate CreateDelegate(System.Type delegateType, object? target) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.ParameterBuilder? DefineParameter(int position, System.Reflection.ParameterAttributes attributes, string? parameterName) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.MethodInfo GetBaseDefinition() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.DynamicILInfo GetDynamicILInfo() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.ILGenerator GetILGenerator() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.ILGenerator GetILGenerator(int streamSize) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.MethodImplAttributes GetMethodImplementationFlags() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.ParameterInfo[] GetParameters() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object? Invoke(object? obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder? binder, object?[]? parameters, System.Globalization.CultureInfo? culture) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsDefined(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override string ToString() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
    }
}
