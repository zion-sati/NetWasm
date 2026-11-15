// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.UnsafeAccessors;

namespace NetWasm.Compiler.Metadata.Tests.UnsafeAccessors;

public sealed class UnsafeAccessorSignatureReaderTests
{
    [Fact]
    public void MethodReaderPreservesModifiersTypicalVariablesAndHeaderFacts()
    {
        using var fixture = new Fixture();
        var signature = fixture.Create().Read(fixture.Method);
        Assert.False(signature.Header.IsInstance);
        Assert.True(signature.Header.IsGeneric);
        Assert.Equal(1, signature.GenericParameterCount);
        Assert.Equal(3, signature.RequiredParameterCount);
        var result = signature.ReturnType;
        Assert.Equal(CliTypeShape.Modified, result.Shape);
        Assert.True(result.IsRequiredModifier);
        Assert.Equal("Test.RequiredModifier", result.CustomModifier!.FullName);
        Assert.Equal(new AssemblyIdentity("Actual"), result.CustomModifier.Assembly);
        Assert.Equal(CliTypeShape.ManagedByReference, result.ElementType!.Shape);
        Assert.Equal(CliTypeIdentity.GenericParameter(false, 0), result.ElementType.ElementType);
        Assert.Collection(signature.ParameterTypes,
            parameter =>
            {
                Assert.Equal(CliTypeShape.Modified, parameter.Shape);
                Assert.False(parameter.IsRequiredModifier);
                Assert.Equal("Test.OptionalModifier", parameter.CustomModifier!.FullName);
                Assert.Equal(CliTypeIdentity.GenericParameter(true, 0), parameter.ElementType);
            },
            parameter =>
            {
                Assert.Equal(CliTypeShape.SzArray, parameter.Shape);
                Assert.Equal(CliTypeIdentity.GenericParameter(false, 0), parameter.ElementType);
            },
            parameter =>
            {
                Assert.Equal("Test.External", parameter.FullName);
                Assert.Equal(new AssemblyIdentity("Actual"), parameter.Assembly);
            });
        Assert.Equal(CliValueKind.Void, fixture.Method.Signature.ReturnType);
    }

    [Fact]
    public void FieldReaderPreservesRequiredModifierAndUnsubstitutedTypeParameter()
    {
        using var fixture = new Fixture();
        var signature = fixture.Create().Read(fixture.Field);
        Assert.Equal(CliTypeShape.Modified, signature.Shape);
        Assert.True(signature.IsRequiredModifier);
        Assert.Equal("Test.RequiredModifier", signature.CustomModifier!.FullName);
        Assert.Equal(CliTypeIdentity.GenericParameter(false, 0), signature.ElementType);
        Assert.Equal(CliValueKind.I4, fixture.Field.SignatureType.StackKind);
    }

    [Fact]
    public void MissingDescriptorsFailBeforeAccessingMetadata()
    {
        using var fixture = new Fixture();
        var reader = fixture.Create();
        Assert.Throws<ArgumentNullException>(() => reader.Read((MethodDefinitionModel)null!));
        Assert.Throws<ArgumentNullException>(() => reader.Read((FieldDefinitionModel)null!));
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly AssemblyIdentity Assembly = new("Signatures");
        private readonly MemoryStream _stream;
        private readonly PEReader _pe;
        private readonly MetadataAssemblySnapshot _snapshot;

        internal Fixture()
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("Signatures.dll"), default, default, default);
            metadata.AddAssembly(metadata.GetOrAddString(Assembly.Name), new(1, 0), default, default, 0, 0);
            var externalAssembly = metadata.AddAssemblyReference(metadata.GetOrAddString("Alias"), new(1, 0), default, default, 0, default);
            var required = metadata.AddTypeReference(externalAssembly, metadata.GetOrAddString("Test"), metadata.GetOrAddString("RequiredModifier"));
            var optional = metadata.AddTypeReference(externalAssembly, metadata.GetOrAddString("Test"), metadata.GetOrAddString("OptionalModifier"));
            var external = metadata.AddTypeReference(externalAssembly, metadata.GetOrAddString("Test"), metadata.GetOrAddString("External"));
            var owner = metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Test"), metadata.GetOrAddString("Owner`1"),
                default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            var methodSignature = new BlobBuilder();
            new BlobEncoder(methodSignature).MethodSignature(genericParameterCount: 1).Parameters(3, returns =>
            {
                returns.CustomModifiers().AddModifier(required, isOptional: false);
                returns.Type(isByRef: true).GenericTypeParameter(0);
            }, parameters =>
            {
                var first = parameters.AddParameter();
                first.CustomModifiers().AddModifier(optional, isOptional: true);
                first.Type().GenericMethodTypeParameter(0);
                parameters.AddParameter().Type().SZArray().GenericTypeParameter(0);
                parameters.AddParameter().Type().Type(external, isValueType: false);
            });
            var method = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static, MethodImplAttributes.IL,
                metadata.GetOrAddString("Read"), metadata.GetOrAddBlob(methodSignature), -1, MetadataTokens.ParameterHandle(1));
            metadata.AddGenericParameter(owner, GenericParameterAttributes.None, metadata.GetOrAddString("T"), 0);
            metadata.AddGenericParameter(method, GenericParameterAttributes.None, metadata.GetOrAddString("U"), 0);
            var fieldSignature = new BlobBuilder();
            var fieldType = new BlobEncoder(fieldSignature).FieldSignature();
            fieldType.CustomModifiers().AddModifier(required, isOptional: false);
            fieldType.GenericTypeParameter(0);
            var field = metadata.AddFieldDefinition(FieldAttributes.Public, metadata.GetOrAddString("Value"), metadata.GetOrAddBlob(fieldSignature));
            var image = new BlobBuilder();
            new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), new()).Serialize(image);
            _stream = new(image.ToArray(), writable: false);
            _pe = new(_stream);
            _snapshot = new(Assembly, _pe.GetMetadataReader(), ImmutableDictionary<int, TypeDefinitionModel>.Empty,
                ImmutableDictionary<int, FieldDefinitionModel>.Empty, ImmutableDictionary<int, MethodDefinitionModel>.Empty,
                ImmutableDictionary<int, EntityHandle>.Empty)
            { AssemblyIdentityAliases = new(ImmutableDictionary<string, string>.Empty.Add("Alias", "Actual")) };
            Method = new(new(Assembly, MetadataTokens.GetToken(method)), new(Assembly, MetadataTokens.GetToken(owner)),
                "Read", true, MethodSignatureModel.Create(CliValueKind.Void), 0);
            Field = new(new(Assembly, MetadataTokens.GetToken(field)), Method.DeclaringType, "Value", CliValueKind.I4, false);
        }

        internal MethodDefinitionModel Method { get; }
        internal FieldDefinitionModel Field { get; }
        internal IUnsafeAccessorSignatureReader Create() => ThroughContract(new UnsafeAccessorSignatureReader(
            ImmutableDictionary<string, MetadataAssemblySnapshot>.Empty.Add(Assembly.Name, _snapshot)));
        private static IUnsafeAccessorSignatureReader ThroughContract(IUnsafeAccessorSignatureReader reader) => reader;
        public void Dispose()
        {
            _pe.Dispose();
            _stream.Dispose();
        }
    }
}
