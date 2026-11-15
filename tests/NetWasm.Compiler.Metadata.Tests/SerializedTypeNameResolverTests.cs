// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class SerializedTypeNameResolverTests
{
    [Theory]
    [InlineData("Example.Same", "[Source]Example.Same")]
    [InlineData("Example.Same, Other", "[Other]Example.Same")]
    [InlineData("Example.Same, Alias", "[Other]Example.Same")]
    [InlineData("System.Int32", "primitive:i4")]
    [InlineData("System.Int32, Core", "primitive:i4")]
    [InlineData("Example.Same[]", "[Source]Example.Same[]")]
    [InlineData("Example.Same[], Other", "[Other]Example.Same[]")]
    [InlineData("Example.Same[*]", "[Source]Example.Same[*]")]
    [InlineData("Example.Same[,]", "[Source]Example.Same[,]")]
    [InlineData("Example.Same*", "[Source]Example.Same*")]
    [InlineData("Example.Same&", "[Source]Example.Same&")]
    public void ResolvesShapesAndAssemblyQualification(string name, string expected)
    {
        var (resolver, source) = Create();
        Assert.Equal(expected, resolver.Resolve(name, source)!.CanonicalName);
    }

    [Fact]
    public void NestedGenericArgumentsKeepTheirOwnAssemblyAndArrayShape()
    {
        var (resolver, source) = Create();
        var result = resolver.Resolve(
            "Example.Outer+Inner`1[[Example.Same[], Other]], Source", source)!;

        Assert.Equal(CliTypeShape.GenericInstantiation, result.Shape);
        Assert.Equal("Example.Outer+Inner`1", result.ElementType!.FullName);
        Assert.Equal("[Other]Example.Same[]", Assert.Single(result.TypeArguments).CanonicalName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Example.Same[")]
    [InlineData("Missing.Type")]
    [InlineData("Example.Same, Missing")]
    [InlineData("Example.Outer+Inner`1[System.Int32,System.Int32]")]
    public void InvalidMissingAndWrongArityNamesAreRejected(string name)
    {
        var (resolver, source) = Create();
        var error = Assert.Throws<CompilerException>(() => resolver.Resolve(name, source));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, error.Diagnostic.Code);
    }

    [Fact]
    public void NullTypeValueRemainsNull()
    {
        var (resolver, source) = Create();
        Assert.Null(resolver.Resolve(null, source));
    }

    private static (ISerializedTypeNameResolver Resolver, MetadataAssemblySnapshot Source) Create()
    {
        TypeDefinitionModel[] types =
        [
            new(new(new("Source"), 1), "Example", "Same", false, [], []),
            new(new(new("Other"), 1), "Example", "Same", false, [], []),
            new(new(new("Core"), 1), "System", "Int32", true, [], []),
            new(new(new("Source"), 2), "Example", "Outer+Inner`1", false, [], []) { GenericArity = 1 },
        ];
        var definitions = new Definitions(types);
        var resolver = new SerializedTypeNameResolver(types.ToImmutableDictionary(
            type => (type.Key.Assembly, type.FullName), type => type.Key), definitions, definitions, new("Core"));
        var source = new MetadataAssemblySnapshot(new("Source"), null!,
            new Dictionary<int, TypeDefinitionModel>(), new Dictionary<int, FieldDefinitionModel>(),
            new Dictionary<int, MethodDefinitionModel>(), new Dictionary<int, System.Reflection.Metadata.EntityHandle>(),
            new Dictionary<int, ImmutableArray<System.Reflection.Metadata.EntityHandle>>())
        {
            AssemblyIdentityAliases = new(ImmutableDictionary<string, string>.Empty.Add("Alias", "Other")),
        };
        return (resolver, source);
    }

    private sealed class Definitions(TypeDefinitionModel[] types) : ITypeDefinitionResolver, ITypeIdentityResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            types.Single(type => CliTypeIdentity.FromDefinition(type).Equals(identity));

        public CliTypeIdentity GetTypeIdentity(EntityKey key) =>
            CliTypeIdentity.FromDefinition(types.Single(type => type.Key == key));
    }
}
