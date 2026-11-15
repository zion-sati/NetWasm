// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
