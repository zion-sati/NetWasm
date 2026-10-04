using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EnumStorageResolverWidthTests
{
    [Theory]
    [InlineData("i1", CliValueKind.I4, 1, 1)]
    [InlineData("u1", CliValueKind.I4, 1, 1)]
    [InlineData("i2", CliValueKind.I4, 2, 2)]
    [InlineData("u2", CliValueKind.I4, 2, 2)]
    [InlineData("char", CliValueKind.I4, 2, 2)]
    [InlineData("i4", CliValueKind.I4, 4, 4)]
    [InlineData("u4", CliValueKind.I4, 4, 4)]
    [InlineData("i8", CliValueKind.I8, 8, 8)]
    [InlineData("u8", CliValueKind.I8, 8, 8)]
    public void ResolvesEveryEnumStorageWidth(
        string name, CliValueKind stackKind, int size, int alignment)
    {
        var assembly = new AssemblyIdentity("EnumStorageWidthTests");
        var enumKey = new EntityKey(assembly, 1);
        var underlying = CliTypeIdentity.Primitive(name, stackKind);
        var enumType = CliTypeIdentity.FromDefinition(new TypeDefinitionModel(
            enumKey, "Tests", "State", true, [], [])
        {
            IsEnum = true,
            EnumUnderlyingType = underlying,
        });
        foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
        {
            var source = new Layouts(
                [new(enumKey, enumType, 7, 0, underlying, false, false, [])],
                new ValueLayout(underlying, size, alignment, []), target);
            var resolver = ThroughContract(new EnumStorageResolver(source, source, source));

            var result = Assert.Single(resolver.Resolve());

            Assert.Equal(7, result.TypeId);
            Assert.Same(enumType, result.EnumType);
            Assert.Equal(underlying, result.UnderlyingType);
            Assert.Equal(size, result.Layout.Size);
            Assert.Equal(alignment, result.Layout.Alignment);
            Assert.Equal(Math.Max(source.Target.ObjectHeaderSize, alignment), result.PayloadOffset);
            Assert.Equal([underlying], source.Requests);
        }
    }

    [Fact]
    public void ReturnsNoStorageWhenNoEnumMetadataIsReachable()
    {
        var source = new Layouts([], null, WasmTarget.Wasm32);
        var resolver = ThroughContract(new EnumStorageResolver(source, source, source));

        Assert.Empty(resolver.Resolve());
        Assert.Empty(source.Requests);
    }

    [Fact]
    public void PreservesTheUnderlyingLayoutFailure()
    {
        var assembly = new AssemblyIdentity("EnumStorageFailureTests");
        var enumKey = new EntityKey(assembly, 1);
        var enumType = CliTypeIdentity.Named(assembly, "Tests", "State", true);
        var underlying = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var source = new Layouts(
            [new(enumKey, enumType, 7, 0, underlying, false, false, [])],
            null, WasmTarget.Wasm32);
        var resolver = ThroughContract(new EnumStorageResolver(source, source, source));

        var exception = Assert.Throws<InvalidOperationException>(() => resolver.Resolve());

        Assert.Equal($"no value layout for '{underlying}'", exception.Message);
        Assert.Equal([underlying], source.Requests);
    }

    private static IEnumStorageResolver ThroughContract(IEnumStorageResolver actor) => actor;

    private sealed class Layouts(
        ImmutableArray<EnumMetadataLayout> metadata,
        ValueLayout? valueLayout,
        WasmTarget target) : ITargetLayout, IValueLayoutProvider, IEnumMetadataSource
    {
        public WasmTargetLayout Target => WasmTargetLayout.For(target);
        public ImmutableArray<EnumMetadataLayout> EnumMetadata => metadata;
        public List<CliTypeIdentity> Requests { get; } = [];

        public ValueLayout GetValueLayout(CliTypeIdentity type)
        {
            Requests.Add(type);
            return valueLayout ?? throw new InvalidOperationException($"no value layout for '{type}'");
        }
    }
}
