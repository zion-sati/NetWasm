// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataMethodRepositoryTests
{
    [Fact]
    public void GetMethodReturnsKnownMethodAndRejectsMissingKey()
    {
        var repository = new MetadataMethodRepository(
            MetadataActorTestData.One(
                MetadataActorTestData.MethodKey,
                MetadataActorTestData.Method),
            MetadataActorTestData.CreateAvailabilityValidator());

        Assert.Same(
            MetadataActorTestData.Method,
            ((IMethodRepository)repository).GetMethod(MetadataActorTestData.MethodKey));
        var exception = Assert.Throws<CompilerException>(() =>
            ((IMethodRepository)repository).GetMethod(new(new("Missing"), 3)));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }
}
