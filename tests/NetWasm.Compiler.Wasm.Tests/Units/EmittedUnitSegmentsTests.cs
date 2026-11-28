// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// Emitted units go into the store's segments as emission captures them, in capture order: no managed copy of each
// unit waits for the publisher, nothing is spooled to disk, and the same captures give the same bytes.
public sealed class EmittedUnitSegmentsTests
{
    private static CompiledUnit Unit(string identity, int size = 4) => new(identity, new(1, 1),
        [new(UnitQueryKind.StringLayout, "shared", new(7, 7))], [.. Enumerable.Repeat((byte)0x01, size)], [], 1)
    {
        FunctionName = identity,
    };

    [Fact]
    public void AUnitIsInASegmentAsSoonAsItIsCaptured()
    {
        var segments = new EmittedUnitSegments();

        segments.Add(Unit("B"));

        Assert.Equal(1, Assert.Single(segments.Writers).Count);
        Assert.Equal(1, segments.Count);
        Assert.Contains("B", segments.Identities);
    }

    [Fact]
    public void SegmentsFollowCaptureOrderAndTheSameCapturesGiveTheSameBytes()
    {
        static byte[] Captured(params string[] identities)
        {
            var segments = new EmittedUnitSegments();
            foreach (var identity in identities) segments.Add(Unit(identity));
            return UnitActorFixture.Pinned(Assert.Single(segments.Writers));
        }
        var state = new CompiledUnitState("units");
        foreach (var identity in new[] { "C", "A", "B" }) state.EmittedSegments.Add(Unit(identity));

        Assert.Equal(["C", "A", "B"], UnitActorFixture.Emitted(state).Select(unit => unit.Identity));
        Assert.Equal(Captured("C", "A", "B"), Captured("C", "A", "B"));
        Assert.NotEqual(Captured("C", "A", "B"), Captured("A", "B", "C"));
    }

    // A segment at capacity starts another, so segments stay inside the format's 32-bit offsets.
    [Fact]
    public void ASegmentAtCapacityStartsAnother()
    {
        var segments = new EmittedUnitSegments(segmentCapacity: 1);

        foreach (var identity in new[] { "A", "B", "C" }) segments.Add(Unit(identity));

        Assert.Equal([1, 1, 1], segments.Writers.Select(writer => writer.Count));
    }
}

// With a sealer, a segment that fills goes to the store's medium as the next starts, so the compile holds one open
// segment; the publisher's change lists the sealed segments under the sequences they were sealed with, without
// writers, and the store their bytes and the manifest make holds every unit.
public sealed class SealedUnitSegmentsTests
{
    private static readonly string[] Identities = ["A", "B", "C"];

    private static CompiledUnit Unit(string identity) => new(identity, new(1, 1),
        [new(UnitQueryKind.StringLayout, "shared", new(7, 7))], [0x01, 0x02, 0x03, 0x04], [], 1)
    {
        FunctionName = identity,
    };

    private sealed class Recording : IUnitSegmentSealer
    {
        public Dictionary<int, byte[]> Sealed { get; } = [];
        public List<string> Locations { get; } = [];

        public void Seal(string location, int sequence, UnitSegmentWriter writer)
        {
            Locations.Add(location);
            Sealed.Add(sequence, UnitActorFixture.Pinned(writer));
        }
    }

    [Fact]
    public void AFullSegmentIsSealedAsTheNextStartsAndTheStoreHoldsEveryUnit()
    {
        var sealer = new Recording();
        var state = new CompiledUnitState("units")
        {
            EmittedSegments = new(segmentCapacity: 1, sealer: sealer, location: "units", firstSequence: 5),
        };
        foreach (var identity in Identities) state.EmittedSegments.Add(Unit(identity));

        // A and B sealed as B and C started; C's segment is the open one.
        Assert.Equal([5, 6], sealer.Sealed.Keys.Order());
        Assert.Equal(["units", "units"], sealer.Locations);
        Assert.Equal(1, Assert.Single(state.EmittedSegments.Writers).Count);

        state.Store = new UnitStoreForSequence(5).Store;
        new CompiledUnitPublisher(state).Publish();

        var change = state.Change!.Value;
        Assert.Equal([5, 6, 7], change.Segments.Select(segment => segment.Sequence));
        Assert.Equal([false, false, true], change.Segments.Select(segment => segment.Writer is not null));
        var bytes = new Dictionary<int, byte[]>(sealer.Sealed) { [7] = UnitActorFixture.Pinned(change.Segments[2].Writer!) };
        using var next = UnitStore.Over(change.Manifest!, sequence => new PinnedUnitSegmentMemory(bytes[sequence]));
        Assert.All(Identities, identity => Assert.NotNull(next.Find(identity)));
        Assert.Equal(3, next.Count);
    }

    // The sequences a sealer used must be the store's next ones at publishing.
    [Fact]
    public void SegmentsSealedUnderOtherSequencesAreRefused()
    {
        var segments = new EmittedUnitSegments(segmentCapacity: 1, sealer: new Recording(), location: "units", firstSequence: 3);
        segments.Add(Unit("A"));
        segments.Add(Unit("B"));

        Assert.Throws<InvalidOperationException>(() => segments.Segments(4));
    }

    // A store whose next sequence is the given one (an empty store starts at 1).
    private sealed class UnitStoreForSequence(int next)
    {
        public UnitStore Store => UnitStore.Over(UnitManifestFormat.Write([], [], next), _ => null);
    }
}
