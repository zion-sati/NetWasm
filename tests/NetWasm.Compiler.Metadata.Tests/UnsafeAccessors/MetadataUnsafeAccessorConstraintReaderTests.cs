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

public sealed class MetadataUnsafeAccessorConstraintReaderTests
{
    [Fact]
    public void ReadsSeparateTypicalGroupsAndCanonicalNominalConstraintKinds()
    {
        using var fixture = new Fixture("valid");
        var constraints = fixture.Create().Read(fixture.Method);
        var type = Assert.Single(constraints.TypeParameters);
        Assert.Equal(GenericParameterAttributes.ReferenceTypeConstraint, type.Attributes);
        Assert.Collection(type.Types,
            local => { Assert.Equal("Test.LocalConstraint", local.FullName); Assert.True(local.IsValueType); },
            external => { Assert.Equal("Test.ExternalConstraint", external.FullName); Assert.True(external.IsValueType); });
        Assert.Equal(2, fixture.ResolvedDefinitions);
        Assert.Equal(2, constraints.MethodParameters.Length);
        var method = constraints.MethodParameters[0];
        Assert.Equal(GenericParameterAttributes.DefaultConstructorConstraint, method.Attributes);
        Assert.Collection(method.Types,
            variable => Assert.Equal(CliTypeIdentity.GenericParameter(false, 0), variable),
            generic =>
            {
                Assert.Equal(CliTypeShape.GenericInstantiation, generic.Shape);
                Assert.Equal("Test.IConstraint`1", generic.ElementType!.FullName);
                Assert.Equal(CliTypeIdentity.GenericParameter(true, 1), Assert.Single(generic.TypeArguments));
            });
        Assert.Empty(constraints.MethodParameters[1].Types);
    }

    [Fact]
    public void NongenericDeclarationsProduceEmptyGroups()
    {
        using var fixture = new Fixture("empty");
        var constraints = fixture.Create().Read(fixture.Method);
        Assert.Empty(constraints.TypeParameters);
        Assert.Empty(constraints.MethodParameters);
        Assert.Equal(0, fixture.ResolvedDefinitions);
    }

    [Theory]
    [InlineData("type-gap")]
    [InlineData("method-gap")]
    public void ParameterNumberGapsCannotBeReinterpretedAsOtherVariables(string shape)
    {
        using var fixture = new Fixture(shape);
        Assert.Throws<BadImageFormatException>(() => fixture.Create().Read(fixture.Method));
    }

    [Fact]
    public void MissingMethodFailsBeforeAccessingMetadata()
    {
        using var fixture = new Fixture("empty");
        Assert.Throws<ArgumentNullException>(() => fixture.Create().Read(null!));
        Assert.Equal(0, fixture.ResolvedDefinitions);
    }

    private sealed class Fixture : IDisposable, ITypeDefinitionResolver
    {
        private static readonly AssemblyIdentity Assembly = new("Constraints");
        private readonly MemoryStream _stream;
        private readonly PEReader _pe;
        private readonly MetadataAssemblySnapshot _snapshot;

        internal Fixture(string shape)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("Constraints.dll"), default, default, default);
            metadata.AddAssembly(metadata.GetOrAddString(Assembly.Name), new(1, 0), default, default, 0, 0);
            var external = metadata.AddAssemblyReference(metadata.GetOrAddString("External"), new(1, 0), default, default, 0, default);
            var reference = metadata.AddTypeReference(external, metadata.GetOrAddString("Test"), metadata.GetOrAddString("ExternalConstraint"));
            var genericReference = metadata.AddTypeReference(external, metadata.GetOrAddString("Test"), metadata.GetOrAddString("IConstraint`1"));
            var owner = metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Test"), metadata.GetOrAddString("Owner"),
                default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            var local = metadata.AddTypeDefinition(TypeAttributes.Public, metadata.GetOrAddString("Test"), metadata.GetOrAddString("LocalConstraint"),
                default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(2));
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature(genericParameterCount: shape == "empty" ? 0 : 2).Parameters(0, returns => returns.Void(), _ => { });
            var method = metadata.AddMethodDefinition(MethodAttributes.Static | MethodAttributes.Public, MethodImplAttributes.IL,
                metadata.GetOrAddString("Access"), metadata.GetOrAddBlob(signature), -1, MetadataTokens.ParameterHandle(1));
            if (shape != "empty")
            {
                var typeParameter = metadata.AddGenericParameter(owner, GenericParameterAttributes.ReferenceTypeConstraint,
                    metadata.GetOrAddString("T"), shape == "type-gap" ? 1 : 0);
                var methodParameter = metadata.AddGenericParameter(method, GenericParameterAttributes.DefaultConstructorConstraint,
                    metadata.GetOrAddString("U"), shape == "method-gap" ? 1 : 0);
                metadata.AddGenericParameter(method, GenericParameterAttributes.None, metadata.GetOrAddString("V"), shape == "method-gap" ? 2 : 1);
                metadata.AddGenericParameterConstraint(typeParameter, local);
                metadata.AddGenericParameterConstraint(typeParameter, reference);
                var variable = new BlobBuilder();
                new BlobEncoder(variable).TypeSpecificationSignature().GenericTypeParameter(0);
                metadata.AddGenericParameterConstraint(methodParameter, metadata.AddTypeSpecification(metadata.GetOrAddBlob(variable)));
                var generic = new BlobBuilder();
                new BlobEncoder(generic).TypeSpecificationSignature().GenericInstantiation(genericReference, 1, false).AddArgument().GenericMethodTypeParameter(1);
                metadata.AddGenericParameterConstraint(methodParameter, metadata.AddTypeSpecification(metadata.GetOrAddBlob(generic)));
            }
            var image = new BlobBuilder();
            new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(metadata), new()).Serialize(image);
            _stream = new(image.ToArray(), writable: false);
            _pe = new(_stream);
            _snapshot = new(Assembly, _pe.GetMetadataReader(), ImmutableDictionary<int, TypeDefinitionModel>.Empty,
                ImmutableDictionary<int, FieldDefinitionModel>.Empty, ImmutableDictionary<int, MethodDefinitionModel>.Empty,
                ImmutableDictionary<int, EntityHandle>.Empty);
            Method = new(new(Assembly, MetadataTokens.GetToken(method)), new(Assembly, MetadataTokens.GetToken(owner)),
                "Access", true, MethodSignatureModel.Create(CliValueKind.Void), 0);
        }

        internal MethodDefinitionModel Method { get; }
        internal int ResolvedDefinitions { get; private set; }
        internal IUnsafeAccessorConstraintReader Create() => ThroughContract(new MetadataUnsafeAccessorConstraintReader(
            ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot>.Empty.Add(Assembly, _snapshot), this));
        private static IUnsafeAccessorConstraintReader ThroughContract(IUnsafeAccessorConstraintReader reader) => reader;
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity)
        {
            ResolvedDefinitions++;
            Assert.False(identity.IsValueType);
            return new(new(identity.Assembly!.Value, 0x02000002), "Test", identity.FullName![5..], true, [], []);
        }
        public void Dispose()
        {
            _pe.Dispose();
            _stream.Dispose();
        }
    }
}
