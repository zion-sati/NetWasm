using System.Reflection;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class NativeImportDeclarationReaderTests
{
    [Fact]
    public void OrdinaryManagedMethodsDoNotActivateNativeInterop()
    {
        using var fixture = Create(pinvoke: false);
        Assert.Null(((INativeImportDeclarationReader)new NativeImportDeclarationReader()).Read(
            fixture.Reader, fixture.Reader.GetMethodDefinition(fixture.Method)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsExactImportIdentityFlagsAndVarargFacts(bool varargs)
    {
        using var fixture = Create(varargs: varargs);
        var declaration = ((INativeImportDeclarationReader)new NativeImportDeclarationReader()).Read(
            fixture.Reader, fixture.Reader.GetMethodDefinition(fixture.Method));

        Assert.NotNull(declaration);
        Assert.Equal("logical-library", declaration.LibraryName);
        Assert.Equal("native_entry", declaration.EntryPoint);
        Assert.Equal(MethodImportAttributes.CallingConventionCDecl |
            MethodImportAttributes.ExactSpelling, declaration.Attributes);
        Assert.Equal(varargs, declaration.IsVarArg);
        Assert.False(declaration.HasMarshalling);
        Assert.False(declaration.SuppressesGcTransition);
        Assert.False(declaration.HasCustomCallingConvention);
    }

    [Fact]
    public void PreservesMissingProviderAndMarshallingAsReachedValidationFacts()
    {
        using var fixture = Create(module: false, marshalling: true);
        var declaration = ((INativeImportDeclarationReader)new NativeImportDeclarationReader()).Read(
            fixture.Reader, fixture.Reader.GetMethodDefinition(fixture.Method));

        Assert.NotNull(declaration);
        Assert.Empty(declaration.LibraryName);
        Assert.True(declaration.HasMarshalling);
    }

    [Theory]
    [InlineData("SuppressGCTransitionAttribute", false)]
    [InlineData("SuppressGCTransitionAttribute", true)]
    [InlineData("UnmanagedCallConvAttribute", false)]
    [InlineData("UnmanagedCallConvAttribute", true)]
    [InlineData("OtherAttribute", false)]
    [InlineData("OtherAttribute", true)]
    [InlineData("WasmImportLinkageAttribute", false)]
    [InlineData("WasmImportLinkageAttribute", true)]
    public void PreservesSuppressionAndNormalizesDefaultConventionAttributes(
        string attribute, bool localConstructor)
    {
        using var fixture = Create(attribute: attribute, localConstructor: localConstructor);
        var declaration = ((INativeImportDeclarationReader)new NativeImportDeclarationReader()).Read(
            fixture.Reader, fixture.Reader.GetMethodDefinition(fixture.Method));

        Assert.NotNull(declaration);
        Assert.Equal(attribute == "SuppressGCTransitionAttribute", declaration.SuppressesGcTransition);
        Assert.Equal(attribute == "WasmImportLinkageAttribute", declaration.HasWasmImportLinkage);
        Assert.False(declaration.HasCustomCallingConvention);
    }

    [Fact]
    public void NormalizesOnlyDocumentedDefaultAndCdeclUnmanagedCallConvMetadata()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileUnsafeSource(
            "NativeImportConventionMetadata",
            """
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public sealed class UnsupportedConvention { }

            public static class Native
            {
                [DllImport("mule", EntryPoint = "default_entry", ExactSpelling = true)]
                [UnmanagedCallConv]
                public static extern int Default(int value);

                [DllImport("mule", EntryPoint = "cdecl_entry", ExactSpelling = true)]
                [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
                public static extern int Cdecl(int value);

                [DllImport("mule", EntryPoint = "unsupported_entry", ExactSpelling = true)]
                [UnmanagedCallConv(CallConvs = new[] { typeof(UnsupportedConvention) })]
                public static extern int Unsupported(int value);

                [DllImport("mule", EntryPoint = "suppressed_entry", ExactSpelling = true)]
                [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition) })]
                public static extern int Suppressed(int value);
            }
            """);

        using var assembly = ManagedAssemblyTestFactory.Load(path);
        var methods = assembly.Methods.Values.ToDictionary(method => method.Name);

        Assert.False(methods["Default"].NativeImport!.HasCustomCallingConvention);
        var cdecl = methods["Cdecl"].NativeImport!;
        Assert.False(cdecl.HasCustomCallingConvention);
        Assert.Equal(
            MethodImportAttributes.CallingConventionCDecl,
            cdecl.Attributes & MethodImportAttributes.CallingConventionMask);
        Assert.True(methods["Unsupported"].NativeImport!.HasCustomCallingConvention);
        Assert.True(methods["Suppressed"].NativeImport!.HasCustomCallingConvention);
    }

    [Fact]
    public void UnrelatedGenericAttributesDoNotClaimNativeBoundarySemantics()
    {
        using var fixture = Create(attribute: "OtherAttribute`1", genericAttribute: true);

        var declaration = ((INativeImportDeclarationReader)new NativeImportDeclarationReader()).Read(
            fixture.Reader, fixture.Reader.GetMethodDefinition(fixture.Method));

        Assert.NotNull(declaration);
        Assert.Equal("logical-library", declaration.LibraryName);
        Assert.Equal("native_entry", declaration.EntryPoint);
        Assert.False(declaration.HasMarshalling);
        Assert.False(declaration.SuppressesGcTransition);
        Assert.False(declaration.HasCustomCallingConvention);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "Other")]
    [InlineData(false, "CallConvs")]
    public void RejectsNonCanonicalUnmanagedCallConvPayloads(
        bool fixedArgument,
        string? namedArgument)
    {
        using var fixture = Create(
            attribute: "UnmanagedCallConvAttribute",
            fixedArgument: fixedArgument,
            namedArgument: namedArgument);

        var declaration = new NativeImportDeclarationReader().Read(
            fixture.Reader,
            fixture.Reader.GetMethodDefinition(fixture.Method));

        Assert.NotNull(declaration);
        Assert.True(declaration.HasCustomCallingConvention);
    }

    [Fact]
    public void RejectsNullSerializedCallingConventionType()
    {
        using var fixture = Create(
            attribute: "UnmanagedCallConvAttribute",
            nullCallConvention: true);

        var declaration = new NativeImportDeclarationReader().Read(
            fixture.Reader,
            fixture.Reader.GetMethodDefinition(fixture.Method));

        Assert.NotNull(declaration);
        Assert.True(declaration.HasCustomCallingConvention);
    }

    private static MetadataFixture Create(
        bool pinvoke = true,
        bool module = true,
        bool varargs = false,
        bool marshalling = false,
        string? attribute = null,
        bool localConstructor = false,
        bool genericAttribute = false,
        bool fixedArgument = false,
        string? namedArgument = null,
        bool nullCallConvention = false)
    {
        var metadata = new MetadataBuilder();
        var owningModule = metadata.AddModule(0, metadata.GetOrAddString("NativeMetadata"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic,
            default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(varargs
            ? SignatureCallingConvention.VarArgs : SignatureCallingConvention.Default)
            .Parameters(0, result => result.Void(), _ => { });
        var method = metadata.AddMethodDefinition(MethodAttributes.Static |
            (pinvoke ? MethodAttributes.PinvokeImpl : 0), MethodImplAttributes.IL,
            metadata.GetOrAddString("Call"), metadata.GetOrAddBlob(signature),
            0, MetadataTokens.ParameterHandle(1));
        if (pinvoke)
        {
            var library = module ? metadata.AddModuleReference(
                metadata.GetOrAddString("logical-library")) : default;
            metadata.AddMethodImport(method, MethodImportAttributes.CallingConventionCDecl |
                MethodImportAttributes.ExactSpelling,
                metadata.GetOrAddString("native_entry"), library);
        }
        if (marshalling)
        {
            var parameter = metadata.AddParameter(ParameterAttributes.HasFieldMarshal,
                metadata.GetOrAddString("result"), 0);
            metadata.AddMarshallingDescriptor(parameter, metadata.GetOrAddBlob((ImmutableArray<byte>)[0x07]));
        }
        if (attribute is not null)
        {
            var typeNamespace = metadata.GetOrAddString("System.Runtime.InteropServices");
            var typeName = metadata.GetOrAddString(attribute);
            EntityHandle constructor;
            if (localConstructor)
            {
                metadata.AddTypeDefinition(TypeAttributes.Public, typeNamespace, typeName,
                    default, MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(2));
                constructor = metadata.AddMethodDefinition(MethodAttributes.Public,
                    MethodImplAttributes.IL, metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(signature), 0, MetadataTokens.ParameterHandle(marshalling ? 2 : 1));
            }
            else
            {
                EntityHandle type = metadata.AddTypeReference(owningModule,
                    typeNamespace, typeName);
                if (genericAttribute)
                {
                    var instantiated = new BlobBuilder();
                    new BlobEncoder(instantiated).TypeSpecificationSignature()
                        .GenericInstantiation(type, 1, isValueType: false)
                        .AddArgument().Int32();
                    type = metadata.AddTypeSpecification(metadata.GetOrAddBlob(instantiated));
                }
                var constructorSignature = new BlobBuilder();
                new BlobEncoder(constructorSignature)
                    .MethodSignature(isInstanceMethod: true)
                    .Parameters(
                        fixedArgument ? 1 : 0,
                        result => result.Void(),
                        parameters =>
                        {
                            if (fixedArgument)
                            {
                                parameters.AddParameter().Type().Int32();
                            }
                        });
                constructor = metadata.AddMemberReference(
                    type,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(constructorSignature));
            }
            var payload = new BlobBuilder();
            payload.WriteUInt16(1);
            if (fixedArgument)
            {
                payload.WriteInt32(1);
            }
            payload.WriteUInt16((ushort)(namedArgument is null && !nullCallConvention ? 0 : 1));
            if (nullCallConvention)
            {
                payload.WriteByte(0x54); // property
                payload.WriteByte(0x1d); // single-dimensional array
                payload.WriteByte(0x50); // System.Type
                payload.WriteSerializedString("CallConvs");
                payload.WriteInt32(1);
                payload.WriteSerializedString(null);
            }
            else if (namedArgument is not null)
            {
                payload.WriteByte(0x54); // property
                payload.WriteByte(0x08); // int32
                payload.WriteSerializedString(namedArgument);
                payload.WriteInt32(1);
            }
            metadata.AddCustomAttribute(
                method,
                constructor,
                metadata.GetOrAddBlob(payload));
        }
        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        return new(MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray()), method);
    }

    private sealed class MetadataFixture(
        MetadataReaderProvider provider,
        MethodDefinitionHandle method) : IDisposable
    {
        public MetadataReader Reader { get; } = provider.GetMetadataReader();
        public MethodDefinitionHandle Method { get; } = method;
        public void Dispose() => provider.Dispose();
    }
}
