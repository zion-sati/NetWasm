// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class NativeImportDeclarationReaderTests
{
    private static readonly string[] HResultFixtureMethods = ["Preserved", "Translated", "Managed"];

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
        Assert.Null(declaration.UnsupportedCallingConvention);
    }

    // A descriptor longer than one native type (an array of I4 here) is marshalling NetWasm cannot express.
    [Fact]
    public void PreservesMissingProviderAndMarshallingAsReachedValidationFacts()
    {
        using var fixture = Create(module: false, marshalling: [0x2A, 0x07]);
        var declaration = ((INativeImportDeclarationReader)new NativeImportDeclarationReader()).Read(
            fixture.Reader, fixture.Reader.GetMethodDefinition(fixture.Method));

        Assert.NotNull(declaration);
        Assert.Empty(declaration.LibraryName);
        Assert.True(declaration.HasMarshalling);
        Assert.Equal(NativeMarshalAsTypes.None, declaration.MarshalAs);
    }

    // One native type a stub converts to or accepts (I4) is recorded for its position; an unknown one (an
    // interface, 0x1C) is marshalling NetWasm cannot express.
    [Theory]
    [InlineData((byte)0x07, false)]
    [InlineData((byte)0x1C, true)]
    public void RecordsASingleNativeTypeItCanExpress(byte nativeType, bool hasMarshalling)
    {
        using var fixture = Create(marshalling: [nativeType]);
        var declaration = ((INativeImportDeclarationReader)new NativeImportDeclarationReader()).Read(
            fixture.Reader, fixture.Reader.GetMethodDefinition(fixture.Method))!;

        Assert.Equal(hasMarshalling, declaration.HasMarshalling);
        Assert.Equal(hasMarshalling ? null : System.Runtime.InteropServices.UnmanagedType.I4, declaration.MarshalAs.Result);
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
    public void IgnoresSuppressionAndReadsAnEmptyUnmanagedCallConv(
        string attribute, bool localConstructor)
    {
        using var fixture = Create(attribute: attribute, localConstructor: localConstructor, winApi: true);
        var declaration = ((INativeImportDeclarationReader)new NativeImportDeclarationReader()).Read(
            fixture.Reader, fixture.Reader.GetMethodDefinition(fixture.Method));

        Assert.NotNull(declaration);
        Assert.Equal(attribute == "WasmImportLinkageAttribute", declaration.HasWasmImportLinkage);
        Assert.Null(declaration.UnsupportedCallingConvention);
    }

    // What desktop .NET 11 does with each: [UnmanagedCallConv] names the convention of a Winapi
    // import, where Cdecl, Stdcall and Thiscall are its C convention, SuppressGCTransition and
    // MemberFunction modify it, an unknown type is ignored, and Fastcall, Swift and two base
    // conventions fail; a DllImport that names its own convention ignores the attribute.
    [Fact]
    public void ReadsTheConventionsUnmanagedCallConvNamesAsDesktopDoes()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileUnsafeSource(
            "NativeImportConventionMetadata",
            """
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public sealed class UnknownConvention { }

            public static class Native
            {
                [DllImport("mule", EntryPoint = "e", ExactSpelling = true), UnmanagedCallConv]
                public static extern int Default(int value);

                [DllImport("mule", EntryPoint = "e", ExactSpelling = true), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
                public static extern int Cdecl(int value);

                [DllImport("mule", EntryPoint = "e", ExactSpelling = true), UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
                public static extern int Stdcall(int value);

                [DllImport("mule", EntryPoint = "e", ExactSpelling = true), UnmanagedCallConv(CallConvs = [typeof(CallConvThiscall), typeof(CallConvMemberFunction)])]
                public static extern int ThiscallMember(int value);

                [DllImport("mule", EntryPoint = "e", ExactSpelling = true), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvSuppressGCTransition)])]
                public static extern int Suppressed(int value);

                [DllImport("mule", EntryPoint = "e", ExactSpelling = true), UnmanagedCallConv(CallConvs = [typeof(UnknownConvention)])]
                public static extern int Unknown(int value);

                [DllImport("mule", EntryPoint = "e", ExactSpelling = true), UnmanagedCallConv(CallConvs = [typeof(CallConvFastcall)])]
                public static extern int Fastcall(int value);

                [DllImport("mule", EntryPoint = "e", ExactSpelling = true), UnmanagedCallConv(CallConvs = [typeof(CallConvSwift)])]
                public static extern int Swift(int value);

                [DllImport("mule", EntryPoint = "e", ExactSpelling = true), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl), typeof(CallConvStdcall)])]
                public static extern int Both(int value);

                [DllImport("mule", EntryPoint = "e", ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
                [UnmanagedCallConv(CallConvs = [typeof(CallConvFastcall)])]
                public static extern int OwnConvention(int value);
            }
            """);

        using var assembly = ManagedAssemblyTestFactory.Load(path);
        var methods = assembly.Methods.Values.ToDictionary(method => method.Name, method => method.NativeImport!);

        foreach (var supported in new[] { "Default", "Cdecl", "Stdcall", "ThiscallMember", "Suppressed", "Unknown" })
        {
            Assert.Null(methods[supported].UnsupportedCallingConvention);
            Assert.Equal(MethodImportAttributes.CallingConventionWinApi,
                methods[supported].Attributes & MethodImportAttributes.CallingConventionMask);
        }
        Assert.Equal("calling convention 'System.Runtime.CompilerServices.CallConvFastcall' is not supported",
            methods["Fastcall"].UnsupportedCallingConvention);
        Assert.Contains("CallConvSwift' is not supported", methods["Swift"].UnsupportedCallingConvention, StringComparison.Ordinal);
        Assert.Contains("are both named", methods["Both"].UnsupportedCallingConvention, StringComparison.Ordinal);
        Assert.Null(methods["OwnConvention"].UnsupportedCallingConvention);
        Assert.Equal(MethodImportAttributes.CallingConventionStdCall,
            methods["OwnConvention"].Attributes & MethodImportAttributes.CallingConventionMask);
    }

    // PreserveSig=false asks for HRESULT translation; an assembly's [DisableRuntimeMarshalling] reaches each of its
    // methods.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsHResultTranslationAndDisabledRuntimeMarshalling(bool disabled)
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileUnsafeSource(
            "NativeImportHResultMetadata" + disabled,
            "using System.Runtime.InteropServices;\n" +
            (disabled ? "[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]\n" : "") +
            """

            public static class Native
            {
                [DllImport("mule", EntryPoint = "e")]
                public static extern int Preserved(int value);
                [DllImport("mule", EntryPoint = "e", PreserveSig = false)]
                public static extern int Translated(int value);
                public static int Managed(int value) => value;
            }
            """);

        using var assembly = ManagedAssemblyTestFactory.Load(path);
        var methods = assembly.Methods.Values.ToDictionary(method => method.Name);

        Assert.False(methods["Preserved"].NativeImport!.TranslatesHResult);
        Assert.True(methods["Translated"].NativeImport!.TranslatesHResult);
        Assert.All(HResultFixtureMethods, name => Assert.Equal(disabled, methods[name].DisablesRuntimeMarshalling));
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
        Assert.Null(declaration.UnsupportedCallingConvention);
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
            namedArgument: namedArgument,
            winApi: true);

        var declaration = new NativeImportDeclarationReader().Read(
            fixture.Reader,
            fixture.Reader.GetMethodDefinition(fixture.Method));

        Assert.NotNull(declaration);
        Assert.Equal("[UnmanagedCallConv] sets more than its CallConvs", declaration.UnsupportedCallingConvention);
    }

    // A DllImport that names its own convention never reads [UnmanagedCallConv], as on desktop .NET.
    [Fact]
    public void AnImportsOwnConventionIgnoresUnmanagedCallConv()
    {
        using var fixture = Create(attribute: "UnmanagedCallConvAttribute", fixedArgument: true);

        var declaration = new NativeImportDeclarationReader().Read(
            fixture.Reader,
            fixture.Reader.GetMethodDefinition(fixture.Method));

        Assert.NotNull(declaration);
        Assert.Null(declaration.UnsupportedCallingConvention);
    }

    // A null type among CallConvs names no convention, as an unknown type does not.
    [Fact]
    public void IgnoresNullSerializedCallingConventionType()
    {
        using var fixture = Create(
            attribute: "UnmanagedCallConvAttribute",
            nullCallConvention: true,
            winApi: true);

        var declaration = new NativeImportDeclarationReader().Read(
            fixture.Reader,
            fixture.Reader.GetMethodDefinition(fixture.Method));

        Assert.NotNull(declaration);
        Assert.Null(declaration.UnsupportedCallingConvention);
    }

    private static MetadataFixture Create(
        bool pinvoke = true,
        bool module = true,
        bool varargs = false,
        byte[]? marshalling = null,
        string? attribute = null,
        bool localConstructor = false,
        bool genericAttribute = false,
        bool fixedArgument = false,
        string? namedArgument = null,
        bool nullCallConvention = false,
        bool winApi = false)
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
            metadata.AddMethodImport(method, (winApi ? MethodImportAttributes.CallingConventionWinApi :
                MethodImportAttributes.CallingConventionCDecl) | MethodImportAttributes.ExactSpelling,
                metadata.GetOrAddString("native_entry"), library);
        }
        if (marshalling is not null)
        {
            var parameter = metadata.AddParameter(ParameterAttributes.HasFieldMarshal,
                metadata.GetOrAddString("result"), 0);
            metadata.AddMarshallingDescriptor(parameter, metadata.GetOrAddBlob(marshalling));
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
                    metadata.GetOrAddBlob(signature), 0, MetadataTokens.ParameterHandle(marshalling is not null ? 2 : 1));
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

    // A generic method signature carries its generic parameter count before the parameter count; the parameters'
    // [In] and [Out] still read in order. (C# never emits a generic P/Invoke, but metadata can hold one.)
    [Fact]
    public void ReadsTheParameterFlowsOfAGenericSignature()
    {
        using var image = new PEReader(ImmutableArray.Create(GenericImportImage()));
        var metadata = image.GetMetadataReader();
        var method = metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(1));

        var declaration = new NativeImportDeclarationReader().Read(metadata, method);

        Assert.NotNull(declaration);
        Assert.Equal("native", declaration.LibraryName);
        Assert.Equal("call", declaration.EntryPoint);
        Assert.Equal(NativeParameterFlow.Out, declaration.ParameterFlows[0]);
        Assert.Equal(NativeParameterFlow.In, declaration.ParameterFlows[1]);
    }

    private static byte[] GenericImportImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Native.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Native"), new(1, 0), default, default, 0, 0);
        var signature = new BlobBuilder();
        new BlobEncoder(signature).MethodSignature(genericParameterCount: 1)
            .Parameters(2, returnType => returnType.Void(), parameters =>
            {
                parameters.AddParameter().Type().Int32();
                parameters.AddParameter().Type().Int32();
            });
        var outParameter = metadata.AddParameter(ParameterAttributes.Out, metadata.GetOrAddString("result"), 1);
        metadata.AddParameter(ParameterAttributes.In, metadata.GetOrAddString("value"), 2);
        var method = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PinvokeImpl | MethodAttributes.HideBySig,
            MethodImplAttributes.PreserveSig, metadata.GetOrAddString("Call"), metadata.GetOrAddBlob(signature), -1,
            outParameter);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), method);
        metadata.AddGenericParameter(method, GenericParameterAttributes.None, metadata.GetOrAddString("T"), 0);
        var module = metadata.AddModuleReference(metadata.GetOrAddString("native"));
        metadata.AddMethodImport(method, MethodImportAttributes.CallingConventionCDecl, metadata.GetOrAddString("call"), module);
        var image = new BlobBuilder();
        new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), new BlobBuilder()).Serialize(image);
        return image.ToArray();
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
