// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class NativeCallbackDeclarationReaderTests
{
    [Fact]
    public void PreservesDefaultCdeclNamedAndUnsupportedFactsWithoutReflection()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileUnsafeSource(
            "NativeCallbackMetadata",
            """
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public static class Callbacks
            {
                [UnmanagedCallersOnly]
                public static int Default(int value) => value;

                [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
                public static int Cdecl(int value) => value;

                [UnmanagedCallersOnly(EntryPoint = "native_entry")]
                public static int Named(int value) => value;

                [UnmanagedCallersOnly(AssociatedSourceType = typeof(Callbacks))]
                public static int Unsupported(int value) => value;

                public static int Managed(int value) => value;
            }
            """);

        using var assembly = ManagedAssemblyTestFactory.Load(path);
        var methods = assembly.Methods.Values.ToDictionary(method => method.Name);

        Assert.Empty(methods["Default"].NativeCallback!.CallingConventions);
        Assert.False(methods["Default"].NativeCallback!.IsVarArg);
        Assert.False(methods["Default"].NativeCallback!.HasUnsupportedNamedArguments);
        Assert.Equal(
            "System.Runtime.CompilerServices.CallConvCdecl",
            Assert.Single(methods["Cdecl"].NativeCallback!.CallingConventions));
        Assert.Equal("native_entry", methods["Named"].NativeCallback!.EntryPoint);
        Assert.True(methods["Unsupported"].NativeCallback!.HasUnsupportedNamedArguments);
        Assert.Null(methods["Managed"].NativeCallback);
    }

    [Fact]
    public void RejectsDuplicateCallbackAttributesAndIgnoresTypeSpecificationOwners()
    {
        using var duplicate = CreateFixture(duplicate: true);
        Assert.Throws<BadImageFormatException>(() =>
            new NativeCallbackDeclarationReader().Read(
                duplicate.Reader,
                duplicate.Reader.GetMethodDefinition(duplicate.Method)));

        using var unrelated = CreateFixture(typeSpecificationOwner: true);
        Assert.Null(new NativeCallbackDeclarationReader().Read(
            unrelated.Reader,
            unrelated.Reader.GetMethodDefinition(unrelated.Method)));

        using var nullConvention = CreateFixture(nullCallConvention: true);
        var declaration = Assert.IsType<NetWasm.Compiler.Core.NativeInterop.NativeCallbackDeclaration>(
            new NativeCallbackDeclarationReader().Read(
                nullConvention.Reader,
                nullConvention.Reader.GetMethodDefinition(nullConvention.Method)));
        Assert.Equal(string.Empty, Assert.Single(declaration.CallingConventions));
    }

    private static MetadataFixture CreateFixture(
        bool duplicate = false,
        bool typeSpecificationOwner = false,
        bool nullCallConvention = false)
    {
        var metadata = new MetadataBuilder();
        var module = metadata.AddModule(
            0,
            metadata.GetOrAddString("CallbackMetadata"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Callbacks"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var methodSignature = new BlobBuilder();
        new BlobEncoder(methodSignature).MethodSignature().Parameters(
            0,
            result => result.Void(),
            _ => { });
        var method = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Callback"),
            metadata.GetOrAddBlob(methodSignature),
            0,
            MetadataTokens.ParameterHandle(1));

        EntityHandle attributeType = metadata.AddTypeReference(
            module,
            metadata.GetOrAddString(typeSpecificationOwner
                ? "Fixtures"
                : "System.Runtime.InteropServices"),
            metadata.GetOrAddString(typeSpecificationOwner
                ? "OtherAttribute`1"
                : "UnmanagedCallersOnlyAttribute"));
        if (typeSpecificationOwner)
        {
            var specification = new BlobBuilder();
            new BlobEncoder(specification).TypeSpecificationSignature()
                .GenericInstantiation(attributeType, 1, isValueType: false)
                .AddArgument().Int32();
            attributeType = metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(specification));
        }
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(0, result => result.Void(), _ => { });
        var constructor = metadata.AddMemberReference(
            attributeType,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        var payload = new BlobBuilder();
        payload.WriteUInt16(1);
        payload.WriteUInt16((ushort)(nullCallConvention ? 1 : 0));
        if (nullCallConvention)
        {
            payload.WriteByte(0x54); // property
            payload.WriteByte(0x1d); // single-dimensional array
            payload.WriteByte(0x50); // System.Type
            payload.WriteSerializedString("CallConvs");
            payload.WriteInt32(1);
            payload.WriteSerializedString(null);
        }
        var payloadHandle = metadata.GetOrAddBlob(payload);
        metadata.AddCustomAttribute(method, constructor, payloadHandle);
        if (duplicate)
        {
            metadata.AddCustomAttribute(method, constructor, payloadHandle);
        }

        var image = new BlobBuilder();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        return new(
            MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray()),
            method);
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
