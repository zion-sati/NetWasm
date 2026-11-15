// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataFieldRepositoryTests
{
    [Fact]
    public void GetFieldReturnsKnownFieldAndRejectsMissingKey()
    {
        var repository = new MetadataFieldRepository(
            MetadataActorTestData.One(
                MetadataActorTestData.FieldKey,
                MetadataActorTestData.Field),
            MetadataActorTestData.CreateAvailabilityValidator());

        Assert.Same(
            MetadataActorTestData.Field,
            ((IFieldRepository)repository).GetField(MetadataActorTestData.FieldKey));
        var exception = Assert.Throws<CompilerException>(() =>
            ((IFieldRepository)repository).GetField(new(new("Missing"), 2)));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }
}
