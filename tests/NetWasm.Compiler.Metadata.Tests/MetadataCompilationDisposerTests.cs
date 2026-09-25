using System.Collections.Immutable;
using Xunit;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataCompilationDisposerTests
{
    [Fact]
    public void DisposeInvalidatesMetadataAndRemainsIdempotent()
    {
        var lifetime = new MetadataLifetime();
        IMetadataAvailabilityValidator availability = new MetadataAvailabilityValidator(lifetime);
        Action<IMetadataCompilationDisposer> contract = disposer =>
        {
            disposer.Dispose();
            disposer.Dispose();
        };

        contract(new MetadataCompilationDisposer(
            ImmutableArray<ManagedAssembly>.Empty,
            lifetime));

        Assert.Throws<ObjectDisposedException>(availability.Validate);
    }
}
