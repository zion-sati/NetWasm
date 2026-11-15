// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Tests;

public sealed class ExplicitValueLayoutResolverTests
{
    private static readonly AssemblyIdentity Assembly = new("Test");
    private static readonly CliTypeIdentity Type = CliTypeIdentity.Named(
        Assembly,
        "Test",
        "Blob",
        true);

    [Fact]
    public void StrategyResolvesFieldlessDeclaredSizeBlobs()
    {
        var layout = Resolve(Definition(declaredSize: 256), []);

        Assert.Equal(Type, layout.Type);
        Assert.Equal(256, layout.Size);
        Assert.Equal(1, layout.Alignment);
        Assert.Empty(layout.ReferenceOffsets);
    }

    [Fact]
    public void StrategyRejectsMissingInputsAndNonExplicitTypes()
    {
        var definition = Definition(declaredSize: 1);
        Assert.Throws<ArgumentNullException>(() =>
            Resolve(null!, definition, []));
        Assert.Throws<ArgumentNullException>(() =>
            Resolve(Type, null!, []));
        Assert.Throws<ArgumentException>(() => Resolve(
            Type,
            definition with { LayoutKind = CliTypeLayoutKind.Sequential },
            []));
    }

    [Fact]
    public void StrategyRejectsUnsupportedExplicitLayoutShapes()
    {
        AssertUnsupported(
            Definition(declaredSize: 1),
            [new FieldDefinitionModel(
                new EntityKey(Assembly, 0x04000001),
                new EntityKey(Assembly, 0x02000001),
                "Value",
                CliTypeIdentity.Primitive("i4", CliValueKind.I4),
                false)]);
        Assert.Equal(1, Resolve(Definition(declaredSize: 0), []).Size);
    }

    [Fact]
    public void StrategyIgnoresPackingWhenThereAreNoFieldsToPack()
    {
        var layout = Resolve(
            Definition(declaredSize: 16) with { PackingSize = 1 },
            []);

        Assert.Equal(16, layout.Size);
        Assert.Equal(1, layout.Alignment);
    }

    [Fact]
    public void FactoryRejectsAMissingExplicitLayoutStrategy()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ValueLayoutResolverFactory(null!));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void UnionsPreserveOverlappingOffsetsAndDeduplicateReferenceRoots(WasmTarget target)
    {
        var layout = WasmTargetLayout.For(target);
        var resolver = Assert.IsAssignableFrom<IExplicitValueLayoutResolver>(new ExplicitValueLayoutResolver());
        var scalar = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var reference = CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference);
        var result = resolver.Resolve(Type, Definition(0),
            [Storage(0, scalar, 4, 4, []), Storage(0, scalar, 4, 4, []),
             Storage(8, reference, layout.AddressSize, layout.AddressSize, [0]),
             Storage(8, reference, layout.AddressSize, layout.AddressSize, [0])], layout);
        Assert.Equal(8 + layout.AddressSize, result.Value.Size);
        Assert.Equal(8, Assert.Single(result.Value.ReferenceOffsets));
        Assert.Equal<int>([0, 0, 8, 8], result.Fields.Select(field => field.Offset));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RejectsReferenceMisalignmentAndReferenceNonReferenceOverlap(WasmTarget target)
    {
        var layout = WasmTargetLayout.For(target);
        var scalar = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var reference = CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference);
        var resolver = Assert.IsAssignableFrom<IExplicitValueLayoutResolver>(new ExplicitValueLayoutResolver());
        Assert.Throws<CompilerException>(() => resolver.Resolve(Type, Definition(0),
            [Storage(1, reference, layout.AddressSize, layout.AddressSize, [0])], layout));
        Assert.Throws<CompilerException>(() => resolver.Resolve(Type, Definition(0),
            [Storage(0, reference, layout.AddressSize, layout.AddressSize, [0]),
             Storage(layout.AddressSize - 1, scalar, 4, 4, [])], layout));
        Assert.Throws<CompilerException>(() => resolver.Resolve(Type, Definition(0),
            [Storage(0, reference, layout.AddressSize, layout.AddressSize, [0]),
             Storage(0, Type, layout.AddressSize * 2, layout.AddressSize, [layout.AddressSize])], layout));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DistinguishesObjectAndManagedByReferenceAliases(WasmTarget target)
    {
        var layout = WasmTargetLayout.For(target);
        var reference = CliTypeIdentity.Primitive("object", CliValueKind.ManagedReference);
        var interior = Storage(0, Type, layout.AddressSize, layout.AddressSize, [0]);
        interior = interior with { Layout = interior.Layout with { ByReferenceOffsets = [0] } };
        var resolver = Assert.IsAssignableFrom<IExplicitValueLayoutResolver>(new ExplicitValueLayoutResolver());
        var result = resolver.Resolve(Type, Definition(0), [interior, interior], layout);
        Assert.Equal(0, Assert.Single(result.Value.ByReferenceOffsets));
        Assert.Equal(0, Assert.Single(result.Value.ReferenceOffsets));
        Assert.Throws<CompilerException>(() => resolver.Resolve(Type, Definition(0),
            [interior, Storage(0, reference, layout.AddressSize, layout.AddressSize, [0])], layout));
    }

    [Fact]
    public void RejectsInvalidExplicitShapesAndExtents()
    {
        var resolver = Assert.IsAssignableFrom<IExplicitValueLayoutResolver>(new ExplicitValueLayoutResolver());
        var scalar = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(Type, Definition(0), [], null!));
        foreach (var definition in new[]
        {
            Definition(-1), Definition(0) with { GenericArity = 1 },
            Definition(0) with { InlineArrayLength = 2 },
        })
            Assert.Throws<CompilerException>(() => resolver.Resolve(Type, definition, [], WasmTargetLayout.Wasm32));
        foreach (var offset in new[] { -1, int.MaxValue })
            Assert.Throws<CompilerException>(() => resolver.Resolve(Type, Definition(0),
                [Storage(offset, scalar, 4, 4, [])], WasmTargetLayout.Wasm32));
        Assert.Throws<CompilerException>(() => resolver.Resolve(Type, Definition(int.MaxValue),
            [Storage(0, Type, 4, 4, [0])], WasmTargetLayout.Wasm32));
    }

    [Fact]
    public void ExplicitPackingControlsAlignmentWithoutMovingOffsets()
    {
        var resolver = Assert.IsAssignableFrom<IExplicitValueLayoutResolver>(new ExplicitValueLayoutResolver());
        var scalar = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var result = resolver.Resolve(Type, Definition(0) with { PackingSize = 1 },
            [Storage(1, scalar, 4, 4, [])], WasmTargetLayout.Wasm32);
        Assert.Equal((5, 1), (result.Value.Size, result.Value.Alignment));
        Assert.Equal(1, Assert.Single(result.Fields).Offset);
    }

    [Theory]
    [InlineData(9, 0, 9)]
    [InlineData(1, 1, 5)]
    public void DeclaredExplicitSizeIsAMinimumWithoutExtraScalarPadding(int size, int offset, int expected)
    {
        var scalar = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var resolver = Assert.IsAssignableFrom<IExplicitValueLayoutResolver>(new ExplicitValueLayoutResolver());
        var result = resolver.Resolve(Type, Definition(size), [Storage(offset, scalar, 4, 4, [])], WasmTargetLayout.Wasm32);
        Assert.Equal(expected, result.Value.Size);
        Assert.Equal(4, result.Value.Alignment);
    }

    private static ExplicitFieldStorage Storage(int offset, CliTypeIdentity type,
        int size, int alignment, ImmutableArray<int> references) => new(
        new FieldDefinitionModel(new(Assembly, 0x04000001), new(Assembly, 0x02000001),
            "Field", type, false)
        { ExplicitOffset = offset },
        new ValueLayout(type, size, alignment, references));

    private static ValueLayout Resolve(
        TypeDefinitionModel definition,
        ImmutableArray<FieldDefinitionModel> fields) =>
        Resolve(Type, definition, fields);

    private static ValueLayout Resolve(
        CliTypeIdentity type,
        TypeDefinitionModel definition,
        ImmutableArray<FieldDefinitionModel> fields) =>
        ((IExplicitValueLayoutResolver)new ExplicitValueLayoutResolver())
        .Resolve(type, definition, [.. fields.Select(field => new ExplicitFieldStorage(
            field, new ValueLayout(field.SignatureType, 4, 4, [])))],
            WasmTargetLayout.Wasm32).Value;

    private static void AssertUnsupported(
        TypeDefinitionModel definition,
        ImmutableArray<FieldDefinitionModel> fields)
    {
        var exception = Assert.Throws<CompilerException>(() =>
            Resolve(definition, fields));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
    }

    private static TypeDefinitionModel Definition(int declaredSize) => new(
        new EntityKey(Assembly, 0x02000001),
        "Test",
        "Blob",
        true,
        [],
        [])
    {
        LayoutKind = CliTypeLayoutKind.Explicit,
        DeclaredSize = declaredSize,
    };
}
