// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler.Tests;

public sealed class MetadataInvariantValidatorTests
{
    [Fact]
    public void AcceptsConsistentMetadataAndRejectsDuplicateTypeIdentity()
    {
        var validator = CreateValidator();
        var types = ValidationTestData.Types();
        var symbols = ValidationTestData.Symbols();
        var snapshot = Snapshot();

        validator.Validate(snapshot, types, symbols);

        var duplicateTypes = snapshot with
        {
            Types = snapshot.Types.Add(snapshot.Types[0]),
        };
        var exception = Assert.Throws<CompilerException>(() => validator.Validate(
            duplicateTypes,
            types,
            symbols));
        Assert.Equal(DiagnosticCode.CompilerInvariant, exception.Diagnostic.Code);
        Assert.Contains("duplicate type definition identity", exception.Message);

        var duplicateMethods = snapshot with
        {
            Methods = snapshot.Methods.Add(snapshot.Methods[0]),
        };
        var methodException = Assert.Throws<CompilerException>(() =>
            validator.Validate(duplicateMethods, types, symbols));
        Assert.Contains("duplicate method definition identity", methodException.Message);

        var foreignMethod = snapshot.EntryAssemblyMethods[0] with
        {
            Key = new EntityKey(
                new AssemblyIdentity("Foreign"),
                snapshot.EntryAssemblyMethods[0].Key.MetadataToken),
        };
        var foreignEntryMethods = snapshot with
        {
            EntryAssemblyMethods = ImmutableArray.Create(foreignMethod),
        };
        var foreignException = Assert.Throws<CompilerException>(() =>
            validator.Validate(foreignEntryMethods, types, symbols));
        Assert.Contains("foreign assembly identity", foreignException.Message);

        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            null!,
            types,
            symbols));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            snapshot,
            null!,
            symbols));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(
            snapshot,
            types,
            null!));
    }

    private static MetadataCompilationSnapshot Snapshot() => new(
        [],
        ValidationTestData.Assembly,
        ImmutableArray.Create(ValidationTestData.Type()),
        ImmutableArray.Create(ValidationTestData.Method()),
        ImmutableArray.Create(ValidationTestData.Method()));

    private static IMetadataInvariantValidator CreateValidator()
    {
        using var services = new ServiceCollection()
            .AddCompilerValidation()
            .BuildServiceProvider();
        return services.GetRequiredService<IMetadataInvariantValidator>();
    }
}
