// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataTypeRepositoryTests
{
    [Fact]
    public void GetTypeDefinitionReturnsKnownTypeAndRejectsMissingKey()
    {
        var repository = new MetadataTypeRepository(
            MetadataActorTestData.One(
                MetadataActorTestData.TypeKey,
                MetadataActorTestData.Type),
            MetadataActorTestData.CreateAvailabilityValidator());

        Assert.Same(
            MetadataActorTestData.Type,
            ((ITypeRepository)repository).GetTypeDefinition(MetadataActorTestData.TypeKey));
        var exception = Assert.Throws<CompilerException>(() =>
            ((ITypeRepository)repository).GetTypeDefinition(new(new("Missing"), 1)));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }
}
