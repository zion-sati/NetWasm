// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce reference sources.
// NetWasm classifies these APIs as intentionally unsupported: they bind, and every member throws
// PlatformNotSupportedException.

#pragma warning disable CS0067, CS0169, CS0618, CS0809, CS8597, CS8618, CS8625, SYSLIB0050, SYSLIB0051

namespace System.Reflection.Emit
{
    public abstract partial class AssemblyBuilder : System.Reflection.Assembly
    {
        protected AssemblyBuilder() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.ObsoleteAttribute("Assembly.CodeBase and Assembly.EscapedCodeBase are only included for .NET Framework compatibility. Use Assembly.Location instead.", DiagnosticId = "SYSLIB0012", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
        [System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute("This member throws an exception for assemblies embedded in a single-file app")]
        public override string? CodeBase { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.MethodInfo? EntryPoint { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string? FullName { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override long HostContext { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsCollectible { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsDynamic { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string Location { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.Module ManifestModule { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool ReflectionOnly { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Defining a dynamic assembly requires dynamic code.")]
        public static System.Reflection.Emit.AssemblyBuilder DefineDynamicAssembly(System.Reflection.AssemblyName name, System.Reflection.Emit.AssemblyBuilderAccess access) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Defining a dynamic assembly requires dynamic code.")]
        public static System.Reflection.Emit.AssemblyBuilder DefineDynamicAssembly(System.Reflection.AssemblyName name, System.Reflection.Emit.AssemblyBuilderAccess access, System.Collections.Generic.IEnumerable<System.Reflection.Emit.CustomAttributeBuilder>? assemblyAttributes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.ModuleBuilder DefineDynamicModule(string name) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.ModuleBuilder DefineDynamicModuleCore(string name);

        public override bool Equals(object? obj) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Collections.Generic.IList<System.Reflection.CustomAttributeData> GetCustomAttributesData() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.ModuleBuilder? GetDynamicModule(string name) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.ModuleBuilder? GetDynamicModuleCore(string name);

        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Types might be removed")]
        public override System.Type[] GetExportedTypes() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute("This member throws an exception for assemblies embedded in a single-file app")]
        public override System.IO.FileStream GetFile(string name) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute("This member throws an exception for assemblies embedded in a single-file app")]
        public override System.IO.FileStream[] GetFiles(bool getResourceModules) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override int GetHashCode() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.Module[] GetLoadedModules(bool getResourceModules) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.ManifestResourceInfo? GetManifestResourceInfo(string resourceName) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override string[] GetManifestResourceNames() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.IO.Stream? GetManifestResourceStream(string name) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.IO.Stream? GetManifestResourceStream(System.Type type, string name) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.Module? GetModule(string name) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.Module[] GetModules(bool getResourceModules) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.AssemblyName GetName(bool copiedName) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Assembly references might be removed")]
        public override System.Reflection.AssemblyName[] GetReferencedAssemblies() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.Assembly GetSatelliteAssembly(System.Globalization.CultureInfo culture) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.Assembly GetSatelliteAssembly(System.Globalization.CultureInfo culture, System.Version? version) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Types might be removed by trimming. If the type name is a string literal, consider using Type.GetType instead.")]
        public override System.Type? GetType(string name, bool throwOnError, bool ignoreCase) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsDefined(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.ConstructorInfo con, byte[] binaryAttribute) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.Emit.CustomAttributeBuilder customBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetCustomAttributeCore(System.Reflection.ConstructorInfo con, System.ReadOnlySpan<byte> binaryAttribute);
    }

    public abstract partial class ConstructorBuilder : System.Reflection.ConstructorInfo
    {
        protected ConstructorBuilder() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.MethodAttributes Attributes { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.CallingConventions CallingConvention { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? DeclaringType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public bool InitLocals { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } set { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        protected abstract bool InitLocalsCore { get; set; }

        public override int MetadataToken { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.RuntimeMethodHandle MethodHandle { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.Module Module { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string Name { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? ReflectedType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public System.Reflection.Emit.ParameterBuilder DefineParameter(int iSequence, System.Reflection.ParameterAttributes attributes, string? strParamName) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.ParameterBuilder DefineParameterCore(int iSequence, System.Reflection.ParameterAttributes attributes, string strParamName);

        public override object[] GetCustomAttributes(bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.ILGenerator GetILGenerator() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.ILGenerator GetILGenerator(int streamSize) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.ILGenerator GetILGeneratorCore(int streamSize);

        public override System.Reflection.MethodImplAttributes GetMethodImplementationFlags() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.ParameterInfo[] GetParameters() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object Invoke(object? obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder? binder, object?[]? parameters, System.Globalization.CultureInfo? culture) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object Invoke(System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder? binder, object?[]? parameters, System.Globalization.CultureInfo? culture) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsDefined(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.ConstructorInfo con, byte[] binaryAttribute) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.Emit.CustomAttributeBuilder customBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetCustomAttributeCore(System.Reflection.ConstructorInfo con, System.ReadOnlySpan<byte> binaryAttribute);

        public void SetImplementationFlags(System.Reflection.MethodImplAttributes attributes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetImplementationFlagsCore(System.Reflection.MethodImplAttributes attributes);

        public override string ToString() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
    }

    public abstract partial class EnumBuilder : System.Reflection.TypeInfo
    {
        protected EnumBuilder() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.Assembly Assembly { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string? AssemblyQualifiedName { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public System.Type? BaseType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? DeclaringType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public string? FullName { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Guid GUID { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsByRefLike { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsConstructedGenericType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public bool IsSZArray { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsTypeDefinition { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.Module Module { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string Name { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public string? Namespace { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? ReflectedType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public System.RuntimeTypeHandle TypeHandle { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public System.Reflection.Emit.FieldBuilder UnderlyingField { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        protected abstract System.Reflection.Emit.FieldBuilder UnderlyingFieldCore { get; }

        public override System.Type UnderlyingSystemType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)]
        public System.Type CreateType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)]
        public System.Reflection.TypeInfo CreateTypeInfo() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)]
        protected abstract System.Reflection.TypeInfo CreateTypeInfoCore();

        public System.Reflection.Emit.FieldBuilder DefineLiteral(string literalName, object? literalValue) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.FieldBuilder DefineLiteralCore(string literalName, object? literalValue);

        protected override System.Reflection.TypeAttributes GetAttributeFlagsImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
        protected override System.Reflection.ConstructorInfo? GetConstructorImpl(System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Reflection.CallingConventions callConvention, System.Type[] types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
        public override System.Reflection.ConstructorInfo[] GetConstructors(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type? GetElementType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type GetEnumUnderlyingType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)]
        public override System.Reflection.EventInfo? GetEvent(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)]
        public override System.Reflection.EventInfo[] GetEvents() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)]
        public override System.Reflection.EventInfo[] GetEvents(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
        public override System.Reflection.FieldInfo? GetField(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
        public override System.Reflection.FieldInfo[] GetFields(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        public override System.Type? GetInterface(string name, bool ignoreCase) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.InterfaceMapping GetInterfaceMap([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)] System.Type interfaceType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        public override System.Type[] GetInterfaces() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override System.Reflection.MemberInfo[] GetMember(string name, System.Reflection.MemberTypes type, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override System.Reflection.MemberInfo[] GetMembers(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
        protected override System.Reflection.MethodInfo? GetMethodImpl(string name, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Reflection.CallingConventions callConvention, System.Type[]? types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
        public override System.Reflection.MethodInfo[] GetMethods(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes)]
        public override System.Type? GetNestedType(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes)]
        public override System.Type[] GetNestedTypes(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Type? GetNullableUnderlyingType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override System.Reflection.PropertyInfo[] GetProperties(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        protected override System.Reflection.PropertyInfo GetPropertyImpl(string name, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Type? returnType, System.Type[]? types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool HasElementTypeImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override object? InvokeMember(string name, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder? binder, object? target, object?[]? args, System.Reflection.ParameterModifier[]? modifiers, System.Globalization.CultureInfo? culture, string[]? namedParameters) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsArrayImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsAssignableFrom([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] System.Reflection.TypeInfo? typeInfo) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsByRefImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsCOMObjectImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsDefined(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsPointerImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsPrimitiveImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsValueTypeImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("The code for an array of the specified type might not be available.")]
        public override System.Type MakeArrayType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("The code for an array of the specified type might not be available.")]
        public override System.Type MakeArrayType(int rank) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type MakeByRefType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type MakePointerType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.ConstructorInfo con, byte[] binaryAttribute) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.Emit.CustomAttributeBuilder customBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetCustomAttributeCore(System.Reflection.ConstructorInfo con, System.ReadOnlySpan<byte> binaryAttribute);
    }

    public abstract partial class EventBuilder
    {
        protected EventBuilder() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void AddOtherMethod(System.Reflection.Emit.MethodBuilder mdBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void AddOtherMethodCore(System.Reflection.Emit.MethodBuilder mdBuilder);

        public void SetAddOnMethod(System.Reflection.Emit.MethodBuilder mdBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetAddOnMethodCore(System.Reflection.Emit.MethodBuilder mdBuilder);

        public void SetCustomAttribute(System.Reflection.ConstructorInfo con, byte[] binaryAttribute) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.Emit.CustomAttributeBuilder customBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetCustomAttributeCore(System.Reflection.ConstructorInfo con, System.ReadOnlySpan<byte> binaryAttribute);

        public void SetRaiseMethod(System.Reflection.Emit.MethodBuilder mdBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetRaiseMethodCore(System.Reflection.Emit.MethodBuilder mdBuilder);

        public void SetRemoveOnMethod(System.Reflection.Emit.MethodBuilder mdBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetRemoveOnMethodCore(System.Reflection.Emit.MethodBuilder mdBuilder);
    }

    public abstract partial class FieldBuilder : System.Reflection.FieldInfo
    {
        protected FieldBuilder() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.FieldAttributes Attributes { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? DeclaringType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.RuntimeFieldHandle FieldHandle { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type FieldType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override int MetadataToken { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.Module Module { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string Name { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? ReflectedType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override object[] GetCustomAttributes(bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object? GetValue(object? obj) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsDefined(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetConstant(object? defaultValue) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetConstantCore(object? defaultValue);

        public void SetCustomAttribute(System.Reflection.ConstructorInfo con, byte[] binaryAttribute) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.Emit.CustomAttributeBuilder customBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetCustomAttributeCore(System.Reflection.ConstructorInfo con, System.ReadOnlySpan<byte> binaryAttribute);

        public void SetOffset(int iOffset) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetOffsetCore(int iOffset);

        public override void SetValue(object? obj, object? val, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder? binder, System.Globalization.CultureInfo? culture) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
    }

    public abstract partial class GenericTypeParameterBuilder : System.Reflection.TypeInfo
    {
        protected GenericTypeParameterBuilder() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.Assembly Assembly { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string? AssemblyQualifiedName { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public System.Type? BaseType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public bool ContainsGenericParameters { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.MethodBase? DeclaringMethod { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? DeclaringType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public string? FullName { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.GenericParameterAttributes GenericParameterAttributes { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override int GenericParameterPosition { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Guid GUID { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsByRefLike { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsConstructedGenericType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsGenericParameter { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public bool IsGenericType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public bool IsGenericTypeDefinition { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public bool IsSZArray { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsTypeDefinition { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override int MetadataToken { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.Module Module { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string Name { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public string? Namespace { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? ReflectedType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public System.RuntimeTypeHandle TypeHandle { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type UnderlyingSystemType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool Equals(object? o) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override System.Reflection.TypeAttributes GetAttributeFlagsImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
        protected override System.Reflection.ConstructorInfo GetConstructorImpl(System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Reflection.CallingConventions callConvention, System.Type[] types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
        public override System.Reflection.ConstructorInfo[] GetConstructors(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type? GetElementType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)]
        public override System.Reflection.EventInfo GetEvent(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)]
        public override System.Reflection.EventInfo[] GetEvents() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)]
        public override System.Reflection.EventInfo[] GetEvents(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
        public override System.Reflection.FieldInfo GetField(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
        public override System.Reflection.FieldInfo[] GetFields(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        new public System.Type[] GetGenericArguments() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type GetGenericTypeDefinition() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override int GetHashCode() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        public override System.Type GetInterface(string name, bool ignoreCase) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.InterfaceMapping GetInterfaceMap([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)] System.Type interfaceType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        public override System.Type[] GetInterfaces() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override System.Reflection.MemberInfo[] GetMember(string name, System.Reflection.MemberTypes type, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override System.Reflection.MemberInfo[] GetMembers(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
        protected override System.Reflection.MethodInfo GetMethodImpl(string name, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Reflection.CallingConventions callConvention, System.Type[]? types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
        public override System.Reflection.MethodInfo[] GetMethods(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes)]
        public override System.Type GetNestedType(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes)]
        public override System.Type[] GetNestedTypes(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Type? GetNullableUnderlyingType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override System.Reflection.PropertyInfo[] GetProperties(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        protected override System.Reflection.PropertyInfo GetPropertyImpl(string name, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Type? returnType, System.Type[]? types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool HasElementTypeImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override object InvokeMember(string name, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder? binder, object? target, object?[]? args, System.Reflection.ParameterModifier[]? modifiers, System.Globalization.CultureInfo? culture, string[]? namedParameters) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsArrayImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsAssignableFrom([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] System.Reflection.TypeInfo? typeInfo) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        new public bool IsAssignableFrom([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] System.Type? c) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsByRefImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsCOMObjectImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsDefined(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsPointerImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsPrimitiveImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        new public bool IsSubclassOf(System.Type c) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsValueTypeImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("The code for an array of the specified type might not be available.")]
        public override System.Type MakeArrayType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("The code for an array of the specified type might not be available.")]
        public override System.Type MakeArrayType(int rank) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type MakeByRefType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("The native code for this instantiation might not be available at runtime.")]
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("If some of the generic arguments are annotated (either with DynamicallyAccessedMembersAttribute, or generic constraints), trimming can't validate that the requirements of those annotations are met.")]
        public override System.Type MakeGenericType(params System.Type[] typeArguments) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type MakePointerType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetBaseTypeConstraint([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? baseTypeConstraint) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetBaseTypeConstraintCore([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] Type? baseTypeConstraint);

        public void SetCustomAttribute(System.Reflection.ConstructorInfo con, byte[] binaryAttribute) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.Emit.CustomAttributeBuilder customBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetCustomAttributeCore(System.Reflection.ConstructorInfo con, System.ReadOnlySpan<byte> binaryAttribute);

        public void SetGenericParameterAttributes(System.Reflection.GenericParameterAttributes genericParameterAttributes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetGenericParameterAttributesCore(System.Reflection.GenericParameterAttributes genericParameterAttributes);

        public void SetInterfaceConstraints(params System.Type[]? interfaceConstraints) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetInterfaceConstraintsCore(params System.Type[]? interfaceConstraints);

        public override string ToString() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
    }

    public abstract partial class MethodBuilder : System.Reflection.MethodInfo
    {
        protected MethodBuilder() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.MethodAttributes Attributes { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.CallingConventions CallingConvention { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool ContainsGenericParameters { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? DeclaringType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public bool InitLocals { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } set { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        protected abstract bool InitLocalsCore { get; set; }

        public override bool IsGenericMethod { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsGenericMethodDefinition { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsSecurityCritical { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsSecuritySafeCritical { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsSecurityTransparent { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override int MetadataToken { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.RuntimeMethodHandle MethodHandle { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.Module Module { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string Name { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? ReflectedType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.ParameterInfo ReturnParameter { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type ReturnType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.ICustomAttributeProvider ReturnTypeCustomAttributes { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public System.Reflection.Emit.GenericTypeParameterBuilder[] DefineGenericParameters(params string[] names) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.GenericTypeParameterBuilder[] DefineGenericParametersCore(params string[] names);

        public System.Reflection.Emit.ParameterBuilder DefineParameter(int position, System.Reflection.ParameterAttributes attributes, string? strParamName) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.ParameterBuilder DefineParameterCore(int position, System.Reflection.ParameterAttributes attributes, string? strParamName);

        public override bool Equals(object? obj) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.MethodInfo GetBaseDefinition() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type[] GetGenericArguments() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.MethodInfo GetGenericMethodDefinition() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override int GetHashCode() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.ILGenerator GetILGenerator() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.ILGenerator GetILGenerator(int size) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.ILGenerator GetILGeneratorCore(int size);

        public override System.Reflection.MethodImplAttributes GetMethodImplementationFlags() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.ParameterInfo[] GetParameters() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object Invoke(object? obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder? binder, object?[]? parameters, System.Globalization.CultureInfo? culture) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsDefined(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("The native code for this instantiation might not be available at runtime.")]
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("If some of the generic arguments are annotated (either with DynamicallyAccessedMembersAttribute, or generic constraints), trimming can't validate that the requirements of those annotations are met.")]
        public override System.Reflection.MethodInfo MakeGenericMethod(params System.Type[] typeArguments) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.ConstructorInfo con, byte[] binaryAttribute) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.Emit.CustomAttributeBuilder customBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetCustomAttributeCore(System.Reflection.ConstructorInfo con, System.ReadOnlySpan<byte> binaryAttribute);

        public void SetImplementationFlags(System.Reflection.MethodImplAttributes attributes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetImplementationFlagsCore(System.Reflection.MethodImplAttributes attributes);

        public void SetParameters(params System.Type[] parameterTypes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetReturnType(System.Type? returnType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetSignature(System.Type? returnType, System.Type[]? returnTypeRequiredCustomModifiers, System.Type[]? returnTypeOptionalCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? parameterTypeRequiredCustomModifiers, System.Type[][]? parameterTypeOptionalCustomModifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetSignatureCore(System.Type? returnType, System.Type[]? returnTypeRequiredCustomModifiers, System.Type[]? returnTypeOptionalCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? parameterTypeRequiredCustomModifiers, System.Type[][]? parameterTypeOptionalCustomModifiers);

        public override string ToString() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
    }

    public abstract partial class ModuleBuilder : System.Reflection.Module
    {
        protected ModuleBuilder() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.Assembly Assembly { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        [System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute("Returns <Unknown> for modules with no file path")]
        public override string FullyQualifiedName { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override int MDStreamVersion { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override int MetadataToken { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Guid ModuleVersionId { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        [System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute("Returns <Unknown> for modules with no file path")]
        public override string Name { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string ScopeName { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public void CreateGlobalFunctions() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void CreateGlobalFunctionsCore();

        public System.Reflection.Emit.EnumBuilder DefineEnum(string name, System.Reflection.TypeAttributes visibility, System.Type underlyingType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.EnumBuilder DefineEnumCore(string name, System.Reflection.TypeAttributes visibility, System.Type underlyingType);

        public System.Reflection.Emit.MethodBuilder DefineGlobalMethod(string name, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.MethodBuilder DefineGlobalMethod(string name, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? requiredReturnTypeCustomModifiers, System.Type[]? optionalReturnTypeCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? requiredParameterTypeCustomModifiers, System.Type[][]? optionalParameterTypeCustomModifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.MethodBuilder DefineGlobalMethodCore(string name, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? requiredReturnTypeCustomModifiers, System.Type[]? optionalReturnTypeCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? requiredParameterTypeCustomModifiers, System.Type[][]? optionalParameterTypeCustomModifiers);

        public System.Reflection.Emit.MethodBuilder DefineGlobalMethod(string name, System.Reflection.MethodAttributes attributes, System.Type? returnType, System.Type[]? parameterTypes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.FieldBuilder DefineInitializedData(string name, byte[] data, System.Reflection.FieldAttributes attributes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.FieldBuilder DefineInitializedDataCore(string name, byte[] data, System.Reflection.FieldAttributes attributes);

        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("P/Invoke marshalling may dynamically access members that could be trimmed.")]
        public System.Reflection.Emit.MethodBuilder DefinePInvokeMethod(string name, string dllName, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes, System.Runtime.InteropServices.CallingConvention nativeCallConv, System.Runtime.InteropServices.CharSet nativeCharSet) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("P/Invoke marshalling may dynamically access members that could be trimmed.")]
        public System.Reflection.Emit.MethodBuilder DefinePInvokeMethod(string name, string dllName, string entryName, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes, System.Runtime.InteropServices.CallingConvention nativeCallConv, System.Runtime.InteropServices.CharSet nativeCharSet) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("P/Invoke marshalling may dynamically access members that could be trimmed.")]
        protected abstract System.Reflection.Emit.MethodBuilder DefinePInvokeMethodCore(string name, string dllName, string entryName, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes, System.Runtime.InteropServices.CallingConvention nativeCallConv, System.Runtime.InteropServices.CharSet nativeCharSet);

        public System.Reflection.Emit.TypeBuilder DefineType(string name) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.TypeBuilder DefineType(string name, System.Reflection.TypeAttributes attr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.TypeBuilder DefineType(string name, System.Reflection.TypeAttributes attr, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.TypeBuilder DefineType(string name, System.Reflection.TypeAttributes attr, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent, int typesize) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.TypeBuilder DefineType(string name, System.Reflection.TypeAttributes attr, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent, System.Reflection.Emit.PackingSize packsize) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.TypeBuilder DefineType(string name, System.Reflection.TypeAttributes attr, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent, System.Reflection.Emit.PackingSize packingSize, int typesize) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.TypeBuilder DefineType(string name, System.Reflection.TypeAttributes attr, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent, System.Type[]? interfaces) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.TypeBuilder DefineTypeCore(string name, System.Reflection.TypeAttributes attr, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent, System.Type[]? interfaces, System.Reflection.Emit.PackingSize packingSize, int typesize);

        public System.Reflection.Emit.FieldBuilder DefineUninitializedData(string name, int size, System.Reflection.FieldAttributes attributes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.FieldBuilder DefineUninitializedDataCore(string name, int size, System.Reflection.FieldAttributes attributes);

        public override bool Equals(object? obj) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.MethodInfo GetArrayMethod(System.Type arrayClass, string methodName, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.MethodInfo GetArrayMethodCore(System.Type arrayClass, string methodName, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes);

        public override object[] GetCustomAttributes(bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Collections.Generic.IList<System.Reflection.CustomAttributeData> GetCustomAttributesData() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Fields might be removed")]
        public override System.Reflection.FieldInfo? GetField(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Fields might be removed")]
        public override System.Reflection.FieldInfo[] GetFields(System.Reflection.BindingFlags bindingFlags) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public abstract int GetFieldMetadataToken(System.Reflection.FieldInfo field);

        public override int GetHashCode() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Methods might be removed")]
        protected override System.Reflection.MethodInfo? GetMethodImpl(string name, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Reflection.CallingConventions callConvention, System.Type[]? types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public abstract int GetMethodMetadataToken(System.Reflection.ConstructorInfo constructor);

        public abstract int GetMethodMetadataToken(System.Reflection.MethodInfo method);

        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Methods might be removed")]
        public override System.Reflection.MethodInfo[] GetMethods(System.Reflection.BindingFlags bindingFlags) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override void GetPEKind(out System.Reflection.PortableExecutableKinds peKind, out System.Reflection.ImageFileMachine machine) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Types might be removed by trimming. If the type name is a string literal, consider using Type.GetType instead.")]
        public override System.Type? GetType(string className) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Types might be removed by trimming. If the type name is a string literal, consider using Type.GetType instead.")]
        public override System.Type? GetType(string className, bool ignoreCase) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Types might be removed by trimming. If the type name is a string literal, consider using Type.GetType instead.")]
        public override System.Type? GetType(string className, bool throwOnError, bool ignoreCase) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Types might be removed")]
        public override System.Type[] GetTypes() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public abstract int GetTypeMetadataToken(System.Type type);

        public abstract int GetSignatureMetadataToken(System.Reflection.Emit.SignatureHelper signature);

        public abstract int GetStringMetadataToken(string stringConstant);

        public override bool IsDefined(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsResource() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Trimming changes metadata tokens")]
        public override System.Reflection.FieldInfo? ResolveField(int metadataToken, System.Type[]? genericTypeArguments, System.Type[]? genericMethodArguments) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Trimming changes metadata tokens")]
        public override System.Reflection.MemberInfo? ResolveMember(int metadataToken, System.Type[]? genericTypeArguments, System.Type[]? genericMethodArguments) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Trimming changes metadata tokens")]
        public override System.Reflection.MethodBase? ResolveMethod(int metadataToken, System.Type[]? genericTypeArguments, System.Type[]? genericMethodArguments) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Trimming changes metadata tokens")]
        public override byte[] ResolveSignature(int metadataToken) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Trimming changes metadata tokens")]
        public override string ResolveString(int metadataToken) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("Trimming changes metadata tokens")]
        public override System.Type ResolveType(int metadataToken, System.Type[]? genericTypeArguments, System.Type[]? genericMethodArguments) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.ConstructorInfo con, byte[] binaryAttribute) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.Emit.CustomAttributeBuilder customBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetCustomAttributeCore(System.Reflection.ConstructorInfo con, System.ReadOnlySpan<byte> binaryAttribute);
    }

    public abstract partial class PropertyBuilder : System.Reflection.PropertyInfo
    {
        protected PropertyBuilder() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.PropertyAttributes Attributes { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool CanRead { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool CanWrite { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? DeclaringType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.Module Module { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string Name { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type PropertyType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? ReflectedType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public void AddOtherMethod(System.Reflection.Emit.MethodBuilder mdBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void AddOtherMethodCore(System.Reflection.Emit.MethodBuilder mdBuilder);

        public override System.Reflection.MethodInfo[] GetAccessors(bool nonPublic) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.MethodInfo? GetGetMethod(bool nonPublic) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.ParameterInfo[] GetIndexParameters() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.MethodInfo? GetSetMethod(bool nonPublic) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object GetValue(object? obj, object?[]? index) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object GetValue(object? obj, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder? binder, object?[]? index, System.Globalization.CultureInfo? culture) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsDefined(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetConstant(object? defaultValue) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetConstantCore(object? defaultValue);

        public void SetCustomAttribute(System.Reflection.ConstructorInfo con, byte[] binaryAttribute) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.Emit.CustomAttributeBuilder customBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetCustomAttributeCore(System.Reflection.ConstructorInfo con, System.ReadOnlySpan<byte> binaryAttribute);

        public void SetGetMethod(System.Reflection.Emit.MethodBuilder mdBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetGetMethodCore(System.Reflection.Emit.MethodBuilder mdBuilder);

        public void SetSetMethod(System.Reflection.Emit.MethodBuilder mdBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetSetMethodCore(System.Reflection.Emit.MethodBuilder mdBuilder);

        public override void SetValue(object? obj, object? value, object?[]? index) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override void SetValue(object? obj, object? value, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder? binder, object?[]? index, System.Globalization.CultureInfo? culture) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
    }

    public abstract partial class TypeBuilder : System.Reflection.TypeInfo
    {
        protected TypeBuilder() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public const int UnspecifiedTypeSize = 0;

        public override System.Reflection.Assembly Assembly { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string? AssemblyQualifiedName { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public System.Type? BaseType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.MethodBase? DeclaringMethod { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type? DeclaringType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public string? FullName { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.GenericParameterAttributes GenericParameterAttributes { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override int GenericParameterPosition { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Guid GUID { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsByRefLike { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsConstructedGenericType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsGenericParameter { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public bool IsGenericType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public bool IsGenericTypeDefinition { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsSecurityCritical { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsSecuritySafeCritical { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsSecurityTransparent { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public bool IsSZArray { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override bool IsTypeDefinition { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override int MetadataToken { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Reflection.Module Module { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override string Name { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        new public string? Namespace { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public System.Reflection.Emit.PackingSize PackingSize { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        protected abstract System.Reflection.Emit.PackingSize PackingSizeCore { get; }

        public override System.Type? ReflectedType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public int Size { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        protected abstract int SizeCore { get; }

        new public System.RuntimeTypeHandle TypeHandle { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public override System.Type UnderlyingSystemType { get { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); } }

        public void AddInterfaceImplementation([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type interfaceType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void AddInterfaceImplementationCore([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type interfaceType);

        [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)]
        public System.Type CreateType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)]
        public System.Reflection.TypeInfo CreateTypeInfo() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)]
        protected abstract System.Reflection.TypeInfo CreateTypeInfoCore();

        public System.Reflection.Emit.ConstructorBuilder DefineConstructor(System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type[]? parameterTypes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.ConstructorBuilder DefineConstructor(System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type[]? parameterTypes, System.Type[][]? requiredCustomModifiers, System.Type[][]? optionalCustomModifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.ConstructorBuilder DefineConstructorCore(System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type[]? parameterTypes, System.Type[][]? requiredCustomModifiers, System.Type[][]? optionalCustomModifiers);

        public System.Reflection.Emit.ConstructorBuilder DefineDefaultConstructor(System.Reflection.MethodAttributes attributes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.ConstructorBuilder DefineDefaultConstructorCore(System.Reflection.MethodAttributes attributes);

        public System.Reflection.Emit.EventBuilder DefineEvent(string name, System.Reflection.EventAttributes attributes, System.Type eventtype) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.EventBuilder DefineEventCore(string name, System.Reflection.EventAttributes attributes, System.Type eventtype);

        public System.Reflection.Emit.FieldBuilder DefineField(string fieldName, System.Type type, System.Reflection.FieldAttributes attributes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.FieldBuilder DefineField(string fieldName, System.Type type, System.Type[]? requiredCustomModifiers, System.Type[]? optionalCustomModifiers, System.Reflection.FieldAttributes attributes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.FieldBuilder DefineFieldCore(string fieldName, System.Type type, System.Type[]? requiredCustomModifiers, System.Type[]? optionalCustomModifiers, System.Reflection.FieldAttributes attributes);

        public System.Reflection.Emit.GenericTypeParameterBuilder[] DefineGenericParameters(params string[] names) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.GenericTypeParameterBuilder[] DefineGenericParametersCore(params string[] names);

        public System.Reflection.Emit.FieldBuilder DefineInitializedData(string name, byte[] data, System.Reflection.FieldAttributes attributes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.FieldBuilder DefineInitializedDataCore(string name, byte[] data, System.Reflection.FieldAttributes attributes);

        public System.Reflection.Emit.MethodBuilder DefineMethod(string name, System.Reflection.MethodAttributes attributes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.MethodBuilder DefineMethod(string name, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.MethodBuilder DefineMethod(string name, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.MethodBuilder DefineMethod(string name, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? returnTypeRequiredCustomModifiers, System.Type[]? returnTypeOptionalCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? parameterTypeRequiredCustomModifiers, System.Type[][]? parameterTypeOptionalCustomModifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.MethodBuilder DefineMethodCore(string name, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? returnTypeRequiredCustomModifiers, System.Type[]? returnTypeOptionalCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? parameterTypeRequiredCustomModifiers, System.Type[][]? parameterTypeOptionalCustomModifiers);

        public System.Reflection.Emit.MethodBuilder DefineMethod(string name, System.Reflection.MethodAttributes attributes, System.Type? returnType, System.Type[]? parameterTypes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void DefineMethodOverride(System.Reflection.MethodInfo methodInfoBody, System.Reflection.MethodInfo methodInfoDeclaration) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void DefineMethodOverrideCore(System.Reflection.MethodInfo methodInfoBody, System.Reflection.MethodInfo methodInfoDeclaration);

        public System.Reflection.Emit.TypeBuilder DefineNestedType(string name) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.TypeBuilder DefineNestedType(string name, System.Reflection.TypeAttributes attr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.TypeBuilder DefineNestedType(string name, System.Reflection.TypeAttributes attr, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.TypeBuilder DefineNestedType(string name, System.Reflection.TypeAttributes attr, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent, int typeSize) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.TypeBuilder DefineNestedType(string name, System.Reflection.TypeAttributes attr, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent, System.Reflection.Emit.PackingSize packSize) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.TypeBuilder DefineNestedType(string name, System.Reflection.TypeAttributes attr, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent, System.Reflection.Emit.PackingSize packSize, int typeSize) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.TypeBuilder DefineNestedType(string name, System.Reflection.TypeAttributes attr, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent, System.Type[]? interfaces) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.TypeBuilder DefineNestedTypeCore(string name, System.Reflection.TypeAttributes attr, [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent, System.Type[]? interfaces, System.Reflection.Emit.PackingSize packSize, int typeSize);

        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("P/Invoke marshalling may dynamically access members that could be trimmed.")]
        public System.Reflection.Emit.MethodBuilder DefinePInvokeMethod(string name, string dllName, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes, System.Runtime.InteropServices.CallingConvention nativeCallConv, System.Runtime.InteropServices.CharSet nativeCharSet) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("P/Invoke marshalling may dynamically access members that could be trimmed.")]
        public System.Reflection.Emit.MethodBuilder DefinePInvokeMethod(string name, string dllName, string entryName, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes, System.Runtime.InteropServices.CallingConvention nativeCallConv, System.Runtime.InteropServices.CharSet nativeCharSet) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("P/Invoke marshalling may dynamically access members that could be trimmed.")]
        public System.Reflection.Emit.MethodBuilder DefinePInvokeMethod(string name, string dllName, string entryName, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? returnTypeRequiredCustomModifiers, System.Type[]? returnTypeOptionalCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? parameterTypeRequiredCustomModifiers, System.Type[][]? parameterTypeOptionalCustomModifiers, System.Runtime.InteropServices.CallingConvention nativeCallConv, System.Runtime.InteropServices.CharSet nativeCharSet) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("P/Invoke marshalling may dynamically access members that could be trimmed.")]
        protected abstract System.Reflection.Emit.MethodBuilder DefinePInvokeMethodCore(string name, string dllName, string entryName, System.Reflection.MethodAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? returnTypeRequiredCustomModifiers, System.Type[]? returnTypeOptionalCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? parameterTypeRequiredCustomModifiers, System.Type[][]? parameterTypeOptionalCustomModifiers, System.Runtime.InteropServices.CallingConvention nativeCallConv, System.Runtime.InteropServices.CharSet nativeCharSet);

        public System.Reflection.Emit.PropertyBuilder DefineProperty(string name, System.Reflection.PropertyAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? parameterTypes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.PropertyBuilder DefineProperty(string name, System.Reflection.PropertyAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? returnTypeRequiredCustomModifiers, System.Type[]? returnTypeOptionalCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? parameterTypeRequiredCustomModifiers, System.Type[][]? parameterTypeOptionalCustomModifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.PropertyBuilder DefineProperty(string name, System.Reflection.PropertyAttributes attributes, System.Type? returnType, System.Type[]? parameterTypes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Reflection.Emit.PropertyBuilder DefineProperty(string name, System.Reflection.PropertyAttributes attributes, System.Type? returnType, System.Type[]? returnTypeRequiredCustomModifiers, System.Type[]? returnTypeOptionalCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? parameterTypeRequiredCustomModifiers, System.Type[][]? parameterTypeOptionalCustomModifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.PropertyBuilder DefinePropertyCore(string name, System.Reflection.PropertyAttributes attributes, System.Reflection.CallingConventions callingConvention, System.Type? returnType, System.Type[]? returnTypeRequiredCustomModifiers, System.Type[]? returnTypeOptionalCustomModifiers, System.Type[]? parameterTypes, System.Type[][]? parameterTypeRequiredCustomModifiers, System.Type[][]? parameterTypeOptionalCustomModifiers);

        public System.Reflection.Emit.ConstructorBuilder DefineTypeInitializer() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.ConstructorBuilder DefineTypeInitializerCore();

        public System.Reflection.Emit.FieldBuilder DefineUninitializedData(string name, int size, System.Reflection.FieldAttributes attributes) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract System.Reflection.Emit.FieldBuilder DefineUninitializedDataCore(string name, int size, System.Reflection.FieldAttributes attributes);

        protected override System.Reflection.TypeAttributes GetAttributeFlagsImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public static System.Reflection.ConstructorInfo GetConstructor(System.Type type, System.Reflection.ConstructorInfo constructor) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
        protected override System.Reflection.ConstructorInfo? GetConstructorImpl(System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Reflection.CallingConventions callConvention, System.Type[] types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)]
        public override System.Reflection.ConstructorInfo[] GetConstructors(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override object[] GetCustomAttributes(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type GetElementType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)]
        public override System.Reflection.EventInfo? GetEvent(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)]
        public override System.Reflection.EventInfo[] GetEvents() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)]
        public override System.Reflection.EventInfo[] GetEvents(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
        public override System.Reflection.FieldInfo? GetField(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public static System.Reflection.FieldInfo GetField(System.Type type, System.Reflection.FieldInfo field) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
        public override System.Reflection.FieldInfo[] GetFields(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        new public System.Type[] GetGenericArguments() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type GetGenericTypeDefinition() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        public override System.Type? GetInterface(string name, bool ignoreCase) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Reflection.InterfaceMapping GetInterfaceMap([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)] System.Type interfaceType) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.Interfaces)]
        public override System.Type[] GetInterfaces() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override System.Reflection.MemberInfo[] GetMember(string name, System.Reflection.MemberTypes type, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override System.Reflection.MemberInfo[] GetMembers(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public static System.Reflection.MethodInfo GetMethod(System.Type type, System.Reflection.MethodInfo method) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
        protected override System.Reflection.MethodInfo? GetMethodImpl(string name, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Reflection.CallingConventions callConvention, System.Type[]? types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
        public override System.Reflection.MethodInfo[] GetMethods(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes)]
        public override System.Type? GetNestedType(string name, System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicNestedTypes | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicNestedTypes)]
        public override System.Type[] GetNestedTypes(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public System.Type? GetNullableUnderlyingType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override System.Reflection.PropertyInfo[] GetProperties(System.Reflection.BindingFlags bindingAttr) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        protected override System.Reflection.PropertyInfo? GetPropertyImpl(string name, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, System.Type? returnType, System.Type[]? types, System.Reflection.ParameterModifier[]? modifiers) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool HasElementTypeImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
        public override object? InvokeMember(string name, System.Reflection.BindingFlags invokeAttr, System.Reflection.Binder? binder, object? target, object?[]? args, System.Reflection.ParameterModifier[]? modifiers, System.Globalization.CultureInfo? culture, string[]? namedParameters) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsArrayImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override bool IsAssignableFrom([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] System.Reflection.TypeInfo? typeInfo) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        new public bool IsAssignableFrom([System.Diagnostics.CodeAnalysis.NotNullWhenAttribute(true)] System.Type? c) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsByRefImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsCOMObjectImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public bool IsCreated() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract bool IsCreatedCore();

        public override bool IsDefined(System.Type attributeType, bool inherit) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsPointerImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected override bool IsPrimitiveImpl() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        new public bool IsSubclassOf(System.Type c) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("The code for an array of the specified type might not be available.")]
        public override System.Type MakeArrayType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("The code for an array of the specified type might not be available.")]
        public override System.Type MakeArrayType(int rank) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type MakeByRefType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        [System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute("The native code for this instantiation might not be available at runtime.")]
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute("If some of the generic arguments are annotated (either with DynamicallyAccessedMembersAttribute, or generic constraints), trimming can't validate that the requirements of those annotations are met.")]
        public override System.Type MakeGenericType(params System.Type[] typeArguments) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public override System.Type MakePointerType() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.ConstructorInfo con, byte[] binaryAttribute) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        public void SetCustomAttribute(System.Reflection.Emit.CustomAttributeBuilder customBuilder) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetCustomAttributeCore(System.Reflection.ConstructorInfo con, System.ReadOnlySpan<byte> binaryAttribute);

        public void SetParent([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent) { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
        protected abstract void SetParentCore([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] System.Type? parent);

        public override string ToString() { throw new global::System.PlatformNotSupportedException("Reflection.Emit dynamic code generation is not available on this NetWasm platform."); }
    }

    [System.FlagsAttribute]
    public enum AssemblyBuilderAccess
    {
        Run = 1,
        RunAndCollect = 9,
    }
}
