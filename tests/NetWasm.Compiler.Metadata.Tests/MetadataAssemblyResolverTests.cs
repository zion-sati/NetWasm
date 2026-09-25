using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataAssemblyResolverTests
{
    [Fact]
    public void ResolveSupportsDirectAndAliasedNamesAndRejectsMissingOrDisposedState()
    {
        var snapshot = Snapshot("Implementation");
        var lifetime = new MetadataLifetime();
        var resolver = new MetadataAssemblyResolver(
            ImmutableDictionary<string, MetadataAssemblySnapshot>.Empty.Add(
                "Implementation",
                snapshot),
            ImmutableDictionary<string, string>.Empty.Add(
                "Facade",
                "Implementation"),
            new MetadataAvailabilityValidator(lifetime));
        var resolve = ((IMetadataAssemblyResolver)resolver).Resolve;

        Assert.Same(snapshot, resolve(new("Implementation")));
        Assert.Same(snapshot, resolve(new("Facade")));
        Assert.Throws<CompilerException>(() => resolve(new("Missing")));
        lifetime.IsDisposed = true;
        Assert.Throws<ObjectDisposedException>(() => resolve(new("Implementation")));
    }

    private static MetadataAssemblySnapshot Snapshot(string name) => new(
        new(name),
        null!,
        new Dictionary<int, TypeDefinitionModel>(),
        new Dictionary<int, FieldDefinitionModel>(),
        new Dictionary<int, MethodDefinitionModel>(),
        new Dictionary<int, System.Reflection.Metadata.EntityHandle>());
}
