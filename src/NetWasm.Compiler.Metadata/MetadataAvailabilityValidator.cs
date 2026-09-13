using System;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataAvailabilityValidator(
    MetadataLifetime lifetime) : IMetadataAvailabilityValidator
{
    public void Validate() =>
        ObjectDisposedException.ThrowIf(lifetime.IsDisposed, lifetime);
}
