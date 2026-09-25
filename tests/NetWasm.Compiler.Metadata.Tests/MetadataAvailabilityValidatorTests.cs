namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataAvailabilityValidatorTests
{
    [Fact]
    public void ValidateAcceptsActiveLifetimeAndRejectsDisposedLifetime()
    {
        var lifetime = new MetadataLifetime();
        var validator = new MetadataAvailabilityValidator(lifetime);

        ((IMetadataAvailabilityValidator)validator).Validate();
        lifetime.IsDisposed = true;
        Assert.Throws<ObjectDisposedException>(() =>
            ((IMetadataAvailabilityValidator)validator).Validate());
    }
}
