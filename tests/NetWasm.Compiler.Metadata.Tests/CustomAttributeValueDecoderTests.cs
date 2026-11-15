// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class CustomAttributeValueDecoderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardsClosedConstructorParametersAndOriginalPayload(bool memberReference)
    {
        using var fixture = new Fixture(memberReference);
        var result = fixture.Decode();
        Assert.Empty(result.FixedArguments);
        Assert.Empty(result.NamedArguments);
        Assert.Equal(1, fixture.MethodCalls);
        Assert.Equal(1, fixture.BlobCalls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RejectsInvalidConstructorSignaturesBeforeReadingPayloads(int shape)
    {
        using var fixture = new Fixture(memberReference: false, shape);
        Assert.Throws<BadImageFormatException>(() => fixture.Decode());
        Assert.Equal(0, fixture.BlobCalls);
        Assert.Equal(shape is 1 or 2 or 4 or 7 ? 0 : 1, fixture.MethodCalls);
    }

    private sealed class Fixture : IDisposable, IMethodInstanceResolver, ICustomAttributeBlobDecoder
    {
        private readonly MetadataReaderProvider _owner;
        private readonly MetadataAssemblySnapshot _source;
        private readonly CustomAttributeDescriptor _attribute;
        private readonly MethodInstanceModel _constructor;
        private readonly CliTypeIdentity _parameter = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        internal int MethodCalls { get; private set; }
        internal int BlobCalls { get; private set; }

        internal Fixture(bool memberReference, int shape = 0)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("AttributeValues"), default, default, default);
            var signature = new BlobBuilder();
            if (shape == 1) new BlobEncoder(signature).FieldSignature().Int32();
            else new BlobEncoder(signature).MethodSignature(genericParameterCount: shape == 2 ? 1 : 0,
                isInstanceMethod: shape != 4).Parameters(1,
                    result => { if (shape == 3) result.Type().Int32(); else result.Void(); },
                    parameters => parameters.AddParameter().Type().Int32());
            var typeHandle = metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Test"),
                metadata.GetOrAddString("Marker"), default, MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            var name = shape == 5 ? "Other" : ".ctor";
            EntityHandle constructor = memberReference
                ? metadata.AddMemberReference(typeHandle, metadata.GetOrAddString(name), metadata.GetOrAddBlob(signature))
                : metadata.AddMethodDefinition(MethodAttributes.Public, MethodImplAttributes.IL,
                    metadata.GetOrAddString(name), metadata.GetOrAddBlob(signature), 0, MetadataTokens.ParameterHandle(1));
            var payload = new BlobBuilder();
            payload.WriteUInt16(1);
            payload.WriteUInt16(0);
            var attribute = metadata.AddCustomAttribute(typeHandle, constructor, metadata.GetOrAddBlob(payload));
            var image = new BlobBuilder();
            new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
            _owner = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
            var assembly = new AssemblyIdentity("AttributeValues");
            _source = new(assembly, _owner.GetMetadataReader(), new Dictionary<int, TypeDefinitionModel>(),
                new Dictionary<int, FieldDefinitionModel>(), new Dictionary<int, MethodDefinitionModel>(),
                new Dictionary<int, EntityHandle>());
            var type = CliTypeIdentity.Named(assembly, "Test", "Marker", false);
            _attribute = new(assembly, MetadataTokens.GetToken(attribute),
                MetadataTokens.GetToken(shape == 7 ? typeHandle : constructor),
                type, new([_parameter], []));
            var methodSignature = MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(
                shape == 3 ? CliValueKind.I4 : CliValueKind.Void), [_parameter]);
            _constructor = new(new(new(assembly, 0x06000001), new(assembly, MetadataTokens.GetToken(typeHandle)),
                name, shape is 4 or 8, methodSignature, 1)
            { GenericArity = shape == 6 ? 1 : 0 },
                type, [], methodSignature);
        }

        internal CustomAttributeValue<CliTypeIdentity> Decode() =>
            ((ICustomAttributeValueDecoder)new CustomAttributeValueDecoder(
                ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot>.Empty.Add(_source.Identity, _source),
                ImmutableDictionary<AssemblyIdentity, ICustomAttributeBlobDecoder>.Empty.Add(_source.Identity, this),
                this)).Decode(_attribute);

        public MethodInstanceModel ResolveMethodInstance(AssemblyIdentity source, int metadataToken,
            string methodDisplayName, int ilOffset, CliGenericContext? genericContext = null)
        {
            Assert.Equal(_attribute.Source, source);
            Assert.Equal(_attribute.ConstructorToken, metadataToken);
            Assert.Equal(_attribute.GenericContext, genericContext);
            MethodCalls++;
            return _constructor;
        }

        public CustomAttributeValue<CliTypeIdentity> Decode(BlobReader value, ImmutableArray<CliTypeIdentity> parameters)
        {
            Assert.Same(_parameter, Assert.Single(parameters));
            Assert.Equal(1, value.ReadUInt16());
            Assert.Equal(0, value.ReadUInt16());
            Assert.Equal(0, value.RemainingBytes);
            BlobCalls++;
            return new([], []);
        }

        public void Dispose() => _owner.Dispose();
    }
}
