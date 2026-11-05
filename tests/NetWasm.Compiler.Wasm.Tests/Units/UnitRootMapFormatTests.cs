using System.Buffers.Binary;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// A unit keeps its method's root map in this format; read back, the map must be the one
// written, anchors and their assignments included, and damaged bytes are refused.
public sealed class UnitRootMapFormatTests
{
    private static readonly EntityKey Method = new(new AssemblyIdentity("Program"), 0x06000001);
    private static readonly CliTypeIdentity Holder = CliTypeIdentity.Named(
        new AssemblyIdentity("Program"), "", "Holder", isValueType: true);

    private static readonly MethodRootMap Anchored = new(
        Method,
        ImmutableDictionary<RootSource, int>.Empty
            .Add(new(RootSourceKind.Local, 1), 0)
            .Add(new(RootSourceKind.StackStorageAnchor, 0, 8), 1),
        ImmutableDictionary<int, SafepointRootMap>.Empty
            .Add(12, new(4, [new(RootSourceKind.Local, 1)], [new(RootSourceKind.EvaluationStack, 0)])))
    {
        AnchorPlan = new(
            [new(new(StackStorageLocationKind.Local, 2), Holder), new(new(StackStorageLocationKind.EvaluationStack, 1), Holder)],
            ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>>.Empty.Add(12,
            [
                new StackStorageAnchorAssignment.Capture(0, 1),
                new StackStorageAnchorAssignment.Copy(1, 0),
                new StackStorageAnchorAssignment.Clear(0),
            ])),
    };

    [Fact]
    public void AnAnchoredMapReadsBackAsItWasWritten()
    {
        var bytes = UnitRootMapFormat.Write(Anchored);

        var read = UnitRootMapFormat.Read(bytes.AsSpan(), Method, name => name == Holder.CanonicalName ? Holder : null);

        Assert.NotNull(read);
        Assert.Equal(bytes, UnitRootMapFormat.Write(read));
        Assert.Equal(Anchored.AnchorPlan.Anchors, read.AnchorPlan.Anchors);
        Assert.Equal(Anchored.AnchorPlan.Assignments[12], read.AnchorPlan.Assignments[12]);
        Assert.Equal(Anchored.Slots, read.Slots);
    }

    // A name is two bytes a character: one longer than a quarter of the bytes after it reads
    // back too, as a namespaced struct's does where no assignments follow.
    [Fact]
    public void AnAnchorOfALongTypeNameReadsBack()
    {
        var shipment = CliTypeIdentity.Named(new AssemblyIdentity("Contoso.Inventory"), "Contoso.Inventory.Warehouse.Logistics",
            "ShippingManifestEntry", isValueType: true);
        var map = Anchored with
        {
            AnchorPlan = new([new(new(StackStorageLocationKind.Local, 2), shipment)],
                ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>>.Empty),
        };

        var read = UnitRootMapFormat.Read(UnitRootMapFormat.Write(map).AsSpan(), Method,
            name => name == shipment.CanonicalName ? shipment : null);

        Assert.Equal(map.AnchorPlan.Anchors, read!.AnchorPlan.Anchors);
    }

    [Fact]
    public void AMapWithoutAnchorsReadsBackWithTheEmptyPlan()
    {
        var plain = Anchored with { AnchorPlan = StackStorageAnchorPlan.Empty };

        var read = UnitRootMapFormat.Read(UnitRootMapFormat.Write(plain).AsSpan(), Method, _ => null);

        Assert.Same(StackStorageAnchorPlan.Empty, read!.AnchorPlan);
    }

    // The anchor names a type this compile cannot name, so the map cannot be restored.
    [Fact]
    public void AMapWhoseAnchorTypeIsUnknownIsNotRead() =>
        Assert.Null(UnitRootMapFormat.Read(UnitRootMapFormat.Write(Anchored).AsSpan(), Method, _ => null));

    [Theory]
    [InlineData("truncated")]
    [InlineData("count")]
    [InlineData("text")]
    [InlineData("short text")]
    [InlineData("negative text")]
    [InlineData("assignment")]
    [InlineData("trailing")]
    public void DamagedMapsAreRefused(string damage)
    {
        var bytes = UnitRootMapFormat.Write(Anchored).ToArray();
        // Words: the slot count and two slots of four; the safepoint count and one safepoint
        // of ten; then the anchor count, whose first anchor's name length follows its location.
        var firstNameLength = 4 * (1 + 2 * 4 + 1 + 10 + 1 + 2);
        // The last offset's three assignments of three words each end the map.
        var firstAssignmentKind = bytes.Length - 4 * 3 * 3;
        bytes = damage switch
        {
            "truncated" => bytes[..^2],
            "count" => Patched(bytes, 0, -1),
            "text" => Patched(bytes, firstNameLength, 1 << 20),
            // A one-character name the bytes end before.
            "short text" => Patched(bytes[..(firstNameLength + 4)], firstNameLength, 1),
            "negative text" => Patched(bytes, firstNameLength, -1),
            "assignment" => Patched(bytes, firstAssignmentKind, 7),
            _ => [.. bytes, 0, 0, 0, 0],
        };

        Assert.Throws<InvalidDataException>(() =>
            UnitRootMapFormat.Read(bytes, Method, name => name == Holder.CanonicalName ? Holder : null));
    }

    [Fact]
    public void AnAssignmentOfAnUnknownKindIsNotWritten()
    {
        var map = Anchored with
        {
            AnchorPlan = Anchored.AnchorPlan with
            {
                Assignments = ImmutableDictionary<int, ImmutableArray<StackStorageAnchorAssignment>>.Empty.Add(12, [new Other(0)]),
            },
        };

        Assert.Throws<NotSupportedException>(() => UnitRootMapFormat.Write(map));
    }

    private static byte[] Patched(byte[] bytes, int at, int value)
    {
        var copy = bytes.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(at), value);
        return copy;
    }

    private sealed record Other(int Anchor) : StackStorageAnchorAssignment(Anchor);
}
