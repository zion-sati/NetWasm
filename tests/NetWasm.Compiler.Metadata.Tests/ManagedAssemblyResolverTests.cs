using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class ManagedAssemblyResolverTests
{
    [Fact]
    public void ResolveSupportsDirectAndAliasedNamesAndRejectsMissingOrDisposedState()
    {
        var assembly = (ManagedAssembly)RuntimeHelpers.GetUninitializedObject(
            typeof(ManagedAssembly));
        var lifetime = new MetadataLifetime();
        var resolver = new ManagedAssemblyResolver(
            ImmutableDictionary<string, ManagedAssembly>.Empty.Add(
                "Implementation",
                assembly),
            ImmutableDictionary<string, string>.Empty.Add(
                "Facade",
                "Implementation"),
            new MetadataAvailabilityValidator(lifetime));
        var resolve = ((IManagedAssemblyResolver)resolver).Resolve;

        Assert.Same(assembly, resolve(new("Implementation")));
        Assert.Same(assembly, resolve(new("Facade")));
        Assert.Throws<CompilerException>(() => resolve(new("Missing")));
        lifetime.IsDisposed = true;
        Assert.Throws<ObjectDisposedException>(() => resolve(new("Implementation")));
    }
}
