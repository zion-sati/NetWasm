// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce reference sources.
// NetWasm classifies these APIs as intentionally unsupported: they bind, and every member throws
// PlatformNotSupportedException.

#pragma warning disable CS0067, CS0169, CS0618, CS0809, CS8597, CS8618, CS8625, SYSLIB0050, SYSLIB0051

namespace System.Reflection.Emit
{
    public partial class CustomAttributeBuilder
    {
        public CustomAttributeBuilder(System.Reflection.ConstructorInfo con, object?[] constructorArgs) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public CustomAttributeBuilder(System.Reflection.ConstructorInfo con, object?[] constructorArgs, System.Reflection.FieldInfo[] namedFields, object?[] fieldValues) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public CustomAttributeBuilder(System.Reflection.ConstructorInfo con, object?[] constructorArgs, System.Reflection.PropertyInfo[] namedProperties, object?[] propertyValues) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public CustomAttributeBuilder(System.Reflection.ConstructorInfo con, object?[] constructorArgs, System.Reflection.PropertyInfo[] namedProperties, object?[] propertyValues, System.Reflection.FieldInfo[] namedFields, object?[] fieldValues) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
    }

    public abstract class ILGenerator
    {
        protected ILGenerator() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public abstract int ILOffset { get; }

        public abstract void BeginCatchBlock(System.Type? exceptionType);

        public abstract void BeginExceptFilterBlock();

        public abstract System.Reflection.Emit.Label BeginExceptionBlock();

        public abstract void BeginFaultBlock();

        public abstract void BeginFinallyBlock();

        public abstract void BeginScope();

        protected static Label CreateLabel(int id) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public virtual System.Reflection.Emit.LocalBuilder DeclareLocal(System.Type localType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public abstract System.Reflection.Emit.LocalBuilder DeclareLocal(System.Type localType, bool pinned);

        public abstract System.Reflection.Emit.Label DefineLabel();

        public abstract void Emit(System.Reflection.Emit.OpCode opcode);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, byte arg);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, double arg);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, short arg);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, int arg);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, long arg);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, System.Reflection.ConstructorInfo con);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, System.Reflection.Emit.Label label);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, System.Reflection.Emit.Label[] labels);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, System.Reflection.Emit.LocalBuilder local);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, System.Reflection.Emit.SignatureHelper signature);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, System.Reflection.FieldInfo field);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, System.Reflection.MethodInfo meth);

        [System.CLSCompliantAttribute(false)]
        public void Emit(System.Reflection.Emit.OpCode opcode, sbyte arg) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public abstract void Emit(System.Reflection.Emit.OpCode opcode, float arg);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, string str);

        public abstract void Emit(System.Reflection.Emit.OpCode opcode, System.Type cls);

        public abstract void EmitCall(System.Reflection.Emit.OpCode opcode, System.Reflection.MethodInfo methodInfo, System.Type[]? optionalParameterTypes);

        public abstract void EmitCalli(System.Reflection.Emit.OpCode opcode, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes, System.Type[]? optionalParameterTypes);

        public abstract void EmitCalli(System.Reflection.Emit.OpCode opcode, System.Runtime.InteropServices.CallingConvention unmanagedCallConv, System.Type? returnType, System.Type[]? parameterTypes);

        public virtual void EmitCalli(System.Type functionPointerType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public virtual void EmitWriteLine(System.Reflection.Emit.LocalBuilder localBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public virtual void EmitWriteLine(System.Reflection.FieldInfo fld) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public virtual void EmitWriteLine(string value) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public abstract void EndExceptionBlock();

        public abstract void EndScope();

        public abstract void MarkLabel(System.Reflection.Emit.Label loc);

        public virtual void ThrowException([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] System.Type excType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public abstract void UsingNamespace(string usingNamespace);
    }

    public abstract class LocalBuilder : System.Reflection.LocalVariableInfo
    {
        protected LocalBuilder() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsPinned { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override int LocalIndex { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type LocalType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public void SetLocalSymInfo(string name) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected virtual void SetLocalSymInfoCore(string name) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
    }

    public abstract partial class ParameterBuilder
    {
        protected ParameterBuilder() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public virtual int Attributes { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public bool IsIn { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public bool IsOptional { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public bool IsOut { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public virtual string? Name { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public virtual int Position { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public virtual void SetConstant(object? defaultValue) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.ConstructorInfo con, byte[] binaryAttribute) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.Emit.CustomAttributeBuilder customBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetCustomAttributeCore(System.Reflection.ConstructorInfo con, System.ReadOnlySpan<byte> binaryAttribute);
    }

    public sealed partial class SignatureHelper
    {
        internal SignatureHelper() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void AddArgument(System.Type clsArgument) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void AddArgument(System.Type argument, bool pinned) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void AddArgument(System.Type argument, System.Type[]? requiredCustomModifiers, System.Type[]? optionalCustomModifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void AddArguments(System.Type[]? arguments, System.Type[][]? requiredCustomModifiers, System.Type[][]? optionalCustomModifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void AddSentinel() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool Equals(object? obj) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public static System.Reflection.Emit.SignatureHelper GetFieldSigHelper(System.Reflection.Module? mod) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override int GetHashCode() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public static System.Reflection.Emit.SignatureHelper GetLocalVarSigHelper() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public static System.Reflection.Emit.SignatureHelper GetLocalVarSigHelper(System.Reflection.Module? mod) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public static System.Reflection.Emit.SignatureHelper GetMethodSigHelper(System.Reflection.CallingConventions callingConvention, System.Type? returnType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public static System.Reflection.Emit.SignatureHelper GetMethodSigHelper(System.Reflection.Module? mod, System.Reflection.CallingConventions callingConvention, System.Type? returnType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public static System.Reflection.Emit.SignatureHelper GetMethodSigHelper(System.Reflection.Module? mod, System.Type? returnType, System.Type[]? parameterTypes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public static System.Reflection.Emit.SignatureHelper GetPropertySigHelper(System.Reflection.Module? mod, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? requiredReturnTypeCustomModifiers, System.Type[]? optionalReturnTypeCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? requiredParameterTypeCustomModifiers, System.Type[][]? optionalParameterTypeCustomModifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public static System.Reflection.Emit.SignatureHelper GetPropertySigHelper(System.Reflection.Module? mod, System.Type? returnType, System.Type[]? parameterTypes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public static System.Reflection.Emit.SignatureHelper GetPropertySigHelper(System.Reflection.Module? mod, System.Type? returnType, System.Type[]? requiredReturnTypeCustomModifiers, System.Type[]? optionalReturnTypeCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? requiredParameterTypeCustomModifiers, System.Type[][]? optionalParameterTypeCustomModifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public byte[] GetSignature() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override string ToString() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
    }
}
