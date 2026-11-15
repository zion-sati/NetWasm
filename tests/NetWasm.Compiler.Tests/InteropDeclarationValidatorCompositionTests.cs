// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Metadata;
using NetWasm.TestInfrastructure;

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Interop;

namespace NetWasm.Compiler.Tests;

public sealed class InteropDeclarationValidatorCompositionTests
{
    [Fact]
    public void ValidationUsesInjectedNarrowMetadataCapabilities()
    {
        using var assets = TestAssets.Create();
        using var compilation = MetadataCompilationTestFactory.Load(
            assets.Application,
            [assets.Library, assets.CoreLib]);
        var validator = new InteropDeclarationValidator(
            new DelegateTypeRecognizerFactory());

        validator.Validate(
            compilation.Snapshot,
            MetadataCompilationTestActors.TypeDefinitions(compilation.Snapshot),
            new BaseTypeResolverFactory().Create(
                MetadataCompilationTestActors.TypeFinder(compilation.Snapshot),
                MetadataCompilationTestActors.TypeIdentities(compilation.Snapshot),
                MetadataCompilationTestActors.BaseTypeIdentities(compilation.Snapshot)),
            MetadataCompilationTestActors.Methods(compilation.Snapshot),
            MetadataCompilationTestActors.Symbols(compilation.Snapshot));

    }
}
