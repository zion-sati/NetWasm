// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce reference sources.
// NetWasm classifies these APIs as intentionally unsupported: they bind, and every member throws
// PlatformNotSupportedException.

#pragma warning disable CS0067, CS0169, CS0618, CS0809, CS8597, CS8618, CS8625, SYSLIB0050, SYSLIB0051

namespace System.Reflection
{
    public partial class TypeDelegator : System.Reflection.TypeInfo
    {
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)]
        protected System.Type typeImpl;

        protected TypeDelegator() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public TypeDelegator([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type delegatingType) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public override System.Reflection.Assembly Assembly { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override string? AssemblyQualifiedName { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        new public System.Type? BaseType { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        new public string? FullName { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override System.Guid GUID { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override bool IsByRefLike { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override bool IsCollectible { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override bool IsConstructedGenericType { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override bool IsFunctionPointer { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override bool IsGenericMethodParameter { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override bool IsGenericTypeParameter { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        new public bool IsSZArray { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override bool IsTypeDefinition { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override bool IsUnmanagedFunctionPointer { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override bool IsVariableBoundArray { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override int MetadataToken { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override System.Reflection.Module Module { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override string Name { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        new public string? Namespace { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        new public System.RuntimeTypeHandle TypeHandle { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        public override System.Type UnderlyingSystemType { get { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); } }

        protected override System.Reflection.TypeAttributes GetAttributeFlagsImpl() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
        protected override System.Reflection.ConstructorInfo? GetConstructorImpl(System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Reflection.CallingConventions callConvention, System.Type[] types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
        public override System.Reflection.ConstructorInfo[] GetConstructors(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public override System.Type? GetElementType() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)]
        public override System.Reflection.EventInfo? GetEvent(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)]
        public override System.Reflection.EventInfo[] GetEvents() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)]
        public override System.Reflection.EventInfo[] GetEvents(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
        public override System.Reflection.FieldInfo? GetField(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
        public override System.Reflection.FieldInfo[] GetFields(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public System.Type[] GetFunctionPointerCallingConventions() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public System.Type[] GetFunctionPointerParameterTypes() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public System.Type GetFunctionPointerReturnType() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        public override System.Type? GetInterface(string name, bool ignoreCase) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public override System.Reflection.InterfaceMapping GetInterfaceMap([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)] System.Type interfaceType) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        public override System.Type[] GetInterfaces() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override System.Reflection.MemberInfo[] GetMember(string name, System.Reflection.MemberTypes type, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override System.Reflection.MemberInfo[] GetMembers(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public System.Reflection.MemberInfo GetMemberWithSameMetadataDefinitionAs(System.Reflection.MemberInfo member) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
        protected override System.Reflection.MethodInfo? GetMethodImpl(string name, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Reflection.CallingConventions callConvention, System.Type[]? types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
        public override System.Reflection.MethodInfo[] GetMethods(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes)]
        public override System.Type? GetNestedType(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes)]
        public override System.Type[] GetNestedTypes(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public System.Type? GetNullableUnderlyingType() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override System.Reflection.PropertyInfo[] GetProperties(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        protected override System.Reflection.PropertyInfo? GetPropertyImpl(string name, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Type? returnType, System.Type[]? types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        protected override bool HasElementTypeImpl() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override object? InvokeMember(string name, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder? binder, object? target, object?[]? args, System.Reflection.ParameterModifier[]? modifiers, System.Globalization.CultureInfo? culture, string[]? namedParameters) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        protected override bool IsArrayImpl() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public override bool IsAssignableFrom([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] System.Reflection.TypeInfo? typeInfo) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        protected override bool IsByRefImpl() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        protected override bool IsCOMObjectImpl() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        public override bool IsDefined(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        protected override bool IsPointerImpl() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        protected override bool IsPrimitiveImpl() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
        protected override bool IsValueTypeImpl() { throw new global::System.PlatformNotSupportedException("Reflection type delegation is not available on this NetWasm platform."); }
    }
}
