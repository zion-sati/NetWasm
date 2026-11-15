// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataSymbolFormatterTests
{
    [Fact]
    public void FormatProducesTypeAndMethodSymbols()
    {
        var formatter = new MetadataSymbolFormatter(
            new MetadataTypeRepository(MetadataActorTestData.One(
                MetadataActorTestData.TypeKey,
                MetadataActorTestData.Type),
                MetadataActorTestData.CreateAvailabilityValidator()));

        Assert.Equal(
            "Test.Namespace.Sample",
            ((ISymbolFormatter)formatter).Format(MetadataActorTestData.TypeKey));
        Assert.Equal(
            "Test.Namespace.Sample::Run",
            ((ISymbolFormatter)formatter).Format(MetadataActorTestData.Method));
    }
}
