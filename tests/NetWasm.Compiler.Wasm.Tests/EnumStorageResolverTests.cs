using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class EnumStorageResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("EnumResolverTests");
    private static readonly EntityKey EnumKey = new(Assembly, 1);
    private static readonly CliTypeIdentity Underlying = CliTypeIdentity.Primitive("u2", CliValueKind.I4);
    private static readonly CliTypeIdentity Named = CliTypeIdentity.FromDefinition(
        new TypeDefinitionModel(EnumKey, "Tests", "Outer`1+Code", true, [], [])
        {
            IsEnum = true,
            GenericArity = 1,
            EnumUnderlyingType = Underlying,
        });

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ResolvesExactClosedEnumIdentitiesWithoutRequestingOpenValueStorage(WasmTarget target)
    {
        var first = CliTypeIdentity.GenericInstantiation(Named,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var second = CliTypeIdentity.GenericInstantiation(Named,
            [CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false)]);
        var layouts = new ResolverLayouts(
            [Entry(Named, 3, true), Entry(first, 7), Entry(second, 9)], target);
        var resolver = ThroughContract(new EnumStorageResolver(layouts, layouts, layouts));

        Assert.Empty(layouts.Requests);
        var storage = resolver.Resolve();

        Assert.Equal([7, 9], storage.Select(item => item.TypeId));
        Assert.Equal([first, second], storage.Select(item => item.EnumType));
        Assert.Same(first, storage[0].EnumType);
        Assert.Same(second, storage[1].EnumType);
        Assert.Equal([Underlying, Underlying], layouts.Requests);
        Assert.All(storage, item =>
        {
            Assert.Equal(Underlying, item.UnderlyingType);
            Assert.Equal(2, item.Layout.Size);
            Assert.Equal(2, item.Layout.Alignment);
            Assert.Equal(layouts.Target.ObjectHeaderSize, item.PayloadOffset);
        });
    }

    [Fact]
    public void OpenDefinitionsDoNotRequestAnyValueLayout()
    {
        var layouts = new ResolverLayouts([Entry(Named, 3, true)], WasmTarget.Wasm32);
        var resolver = ThroughContract(new EnumStorageResolver(layouts, layouts, layouts));

        Assert.Empty(resolver.Resolve());
        Assert.Empty(layouts.Requests);
    }

    [Fact]
    public void RejectsNullDependenciesBeforeResolvingStorage()
    {
        var layouts = new ResolverLayouts([], WasmTarget.Wasm32);

        Assert.Throws<ArgumentNullException>(() => new EnumStorageResolver(null!, layouts, layouts));
        Assert.Throws<ArgumentNullException>(() => new EnumStorageResolver(layouts, null!, layouts));
        Assert.Throws<ArgumentNullException>(() => new EnumStorageResolver(layouts, layouts, null!));
        Assert.Empty(layouts.Requests);
    }

    private static EnumMetadataLayout Entry(CliTypeIdentity identity, int typeId, bool open = false) =>
        new(EnumKey, identity, typeId, 0, Underlying, false, open, []);

    private static IEnumStorageResolver ThroughContract(IEnumStorageResolver actor) => actor;

    private sealed class ResolverLayouts(
        ImmutableArray<EnumMetadataLayout> metadata,
        WasmTarget target) : ITargetLayout, IValueLayoutProvider, IEnumMetadataSource
    {
        public WasmTargetLayout Target => WasmTargetLayout.For(target);
        public ImmutableArray<EnumMetadataLayout> EnumMetadata => metadata;
        public List<CliTypeIdentity> Requests { get; } = [];

        public ValueLayout GetValueLayout(CliTypeIdentity type)
        {
            Requests.Add(type);
            Assert.Equal(Underlying, type);
            return new(type, 2, 2, []);
        }
    }
}
