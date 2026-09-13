using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

public sealed class CanonicalAbiMemoryLayoutPlannerTests
{
    private readonly CanonicalAbiMemoryLayoutPlanner _planner = new();

    [Theory]
    [InlineData(WasmTarget.Wasm32, 16, 4, 4, 12)]
    [InlineData(WasmTarget.Wasm64, 32, 8, 8, 24)]
    public void LaysOutRecordsForTheSelectedAddressWidth(
        WasmTarget target,
        int size,
        int alignment,
        int textOffset,
        int finalOffset)
    {
        var type = Type(CanonicalAbiTypeKind.Record) with
        {
            Fields =
            [
                new("small", Type(CanonicalAbiTypeKind.U8)),
                new("text", Type(CanonicalAbiTypeKind.Text)),
                new("final", Type(CanonicalAbiTypeKind.U16)),
            ],
        };

        var layout = Plan(_planner, type, target);

        Assert.Equal(size, layout.Size);
        Assert.Equal(alignment, layout.Alignment);
        Assert.Equal([0, textOffset, finalOffset],
            layout.Fields.Select(field => field.Offset));
        var first = layout.Fields[0];
        Assert.Equal("small", first.Field.Name);
        Assert.Same(type.Fields[0].Type, first.Field.Type);
        Assert.Equal(1, first.Layout.Size);
    }

    [Fact]
    public void LaysOutVariantsUsingTheLargestAlignedPayload()
    {
        var type = Type(CanonicalAbiTypeKind.Variant) with
        {
            Cases =
            [
                new("none", null),
                new("number", Type(CanonicalAbiTypeKind.U16)),
                new("wide", Type(CanonicalAbiTypeKind.U64)),
            ],
        };

        var layout = Plan(_planner, type, WasmTarget.Wasm32);

        Assert.Equal(16, layout.Size);
        Assert.Equal(8, layout.Alignment);
        Assert.Equal(1, layout.DiscriminantSize);
        Assert.Equal(8, layout.PayloadOffset);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(8, 1, 1)]
    [InlineData(9, 2, 2)]
    [InlineData(17, 4, 4)]
    [InlineData(65, 12, 4)]
    public void LaysOutFlagsWithoutAnArbitraryCountLimit(
        int count,
        int size,
        int alignment)
    {
        var layout = Plan(_planner, Type(CanonicalAbiTypeKind.Flags) with
        {
            FlagsCount = count,
        }, WasmTarget.Wasm32);

        Assert.Equal(size, layout.Size);
        Assert.Equal(alignment, layout.Alignment);
    }

    [Fact]
    public void LaysOutEveryCanonicalAbiShape()
    {
        var scalarCases = new[]
        {
            (CanonicalAbiTypeKind.Unit, 0, 1),
            (CanonicalAbiTypeKind.Bool, 1, 1),
            (CanonicalAbiTypeKind.S8, 1, 1),
            (CanonicalAbiTypeKind.U8, 1, 1),
            (CanonicalAbiTypeKind.S16, 2, 2),
            (CanonicalAbiTypeKind.U16, 2, 2),
            (CanonicalAbiTypeKind.S32, 4, 4),
            (CanonicalAbiTypeKind.U32, 4, 4),
            (CanonicalAbiTypeKind.F32, 4, 4),
            (CanonicalAbiTypeKind.Character, 4, 4),
            (CanonicalAbiTypeKind.OwnedResource, 4, 4),
            (CanonicalAbiTypeKind.BorrowedResource, 4, 4),
            (CanonicalAbiTypeKind.S64, 8, 8),
            (CanonicalAbiTypeKind.U64, 8, 8),
            (CanonicalAbiTypeKind.F64, 8, 8),
        };
        foreach (var (kind, size, alignment) in scalarCases)
        {
            var layout = Plan(_planner, Type(kind), WasmTarget.Wasm32);
            Assert.Equal(size, layout.Size);
            Assert.Equal(alignment, layout.Alignment);
        }

        foreach (var kind in new[] { CanonicalAbiTypeKind.Text, CanonicalAbiTypeKind.List })
        {
            Assert.Equal(8, Plan(_planner, Type(kind), WasmTarget.Wasm32).Size);
            Assert.Equal(16, Plan(_planner, Type(kind), WasmTarget.Wasm64).Size);
        }

        var alias = Type(CanonicalAbiTypeKind.Alias) with
        {
            ElementType = Type(CanonicalAbiTypeKind.U16),
        };
        Assert.Equal(2, Plan(_planner, alias, WasmTarget.Wasm32).Size);

        var tuple = Type(CanonicalAbiTypeKind.Tuple) with
        {
            Fields = [new("number", Type(CanonicalAbiTypeKind.U32))],
        };
        Assert.Equal(4, Plan(_planner, tuple, WasmTarget.Wasm32).Size);
        Assert.Equal(0, Plan(_planner,
            Type(CanonicalAbiTypeKind.Record), WasmTarget.Wasm32).Size);

        var option = Type(CanonicalAbiTypeKind.Option) with
        {
            ElementType = Type(CanonicalAbiTypeKind.U64),
        };
        Assert.Equal(16, Plan(_planner, option, WasmTarget.Wasm32).Size);

        var result = Type(CanonicalAbiTypeKind.Result) with
        {
            SuccessType = Type(CanonicalAbiTypeKind.U16),
            ErrorType = Type(CanonicalAbiTypeKind.U32),
        };
        Assert.Equal(8, Plan(_planner, result, WasmTarget.Wasm32).Size);

        var payloadless = Type(CanonicalAbiTypeKind.Variant) with
        {
            Cases = [new("only", null)],
        };
        var payloadlessLayout = Plan(_planner, payloadless, WasmTarget.Wasm32);
        Assert.Equal(1, payloadlessLayout.Size);
        Assert.Equal(1, payloadlessLayout.Alignment);
        Assert.Equal(1, payloadlessLayout.PayloadOffset);
        Assert.Equal("only", payloadless.Cases[0].Name);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(257, 2)]
    [InlineData(65537, 4)]
    public void SelectsTheSmallestValidDiscriminant(int count, int expectedSize)
    {
        var cases = ImmutableArray.CreateRange(Enumerable.Range(0, count)
            .Select(_ => new CanonicalAbiCase("case", null)));
        var type = Type(CanonicalAbiTypeKind.Enum) with { Cases = cases };

        var layout = Plan(_planner, type, WasmTarget.Wasm32);

        Assert.Equal(expectedSize, layout.Size);
        Assert.Equal(expectedSize, layout.Alignment);
    }

    [Fact]
    public void RejectsInvalidTypesAndCountsDeterministically()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Plan(_planner, null!, WasmTarget.Wasm32));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Plan(_planner, Type((CanonicalAbiTypeKind)int.MaxValue), WasmTarget.Wasm32));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Plan(_planner, Type(CanonicalAbiTypeKind.Flags) with { FlagsCount = -1 },
                WasmTarget.Wasm32));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Plan(_planner, Type(CanonicalAbiTypeKind.Enum), WasmTarget.Wasm32));
    }

    private static CanonicalAbiType Type(CanonicalAbiTypeKind kind) =>
        new(kind, CliTypeIdentity.FromStackKind(CliValueKind.Unknown));

    private static CanonicalAbiMemoryLayout Plan<TPlanner>(
        TPlanner planner,
        CanonicalAbiType type,
        WasmTarget target)
        where TPlanner : ICanonicalAbiMemoryLayoutPlanner => planner.Plan(type, target);
}
