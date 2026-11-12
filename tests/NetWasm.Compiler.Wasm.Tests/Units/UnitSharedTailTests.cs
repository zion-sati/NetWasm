using System.Buffers.Binary;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

// Sites whose answers end in the same matching types or targets store that tail once in a segment,
// and each answer reads back as it was encoded (issue 229).
public sealed class UnitSharedTailTests
{
    private static readonly ImmutableArray<CliTypeIdentity> Matching = [.. Enumerable.Range(0, 24).Select(static index =>
        CliTypeIdentity.Named(new AssemblyIdentity("Program"), "Shapes", $"Shape{index:D2}", isValueType: false))];

    [Fact]
    public void SitesEndingInOneTailStoreItOnceAndReadBackAsEncoded()
    {
        var (bytes, first, second) = Written();
        var encoded = Encode(first);
        var tail = encoded.AsSpan(UnitAnswerCodec.TailStart(first.Kind, encoded));

        Assert.Equal(1, Occurrences(bytes, tail));
        using var segment = UnitSegment.Over(1, new PinnedUnitSegmentMemory(Pinned(bytes)))!;
        Assert.Equal(encoded, segment.Query(0).Value.ToArray());
        Assert.Equal(Encode(second), segment.Query(1).Value.ToArray());
        Assert.NotEqual(encoded, Encode(second));
    }

    // A shared answer's head ends in its tail's reference; one too short to hold it is corrupt.
    [Fact]
    public void ASharedAnswerTooShortForItsReferenceIsCorrupt()
    {
        var (bytes, _, _) = Written();
        var row = (int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(48));
        var value = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(row + 32));
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(row + 32), (value & 0xFFFFFFFFL) | (4L << 32));

        using var segment = UnitSegment.Over(1, new PinnedUnitSegmentMemory(Pinned(bytes)))!;
        Assert.Throws<InvalidDataException>(() => segment.Query(0));
    }

    private static (byte[] Bytes, UnitDependency First, UnitDependency Second) Written()
    {
        var (first, second) = (Site(4), Site(12));
        var writer = new UnitSegmentWriter();
        Assert.True(writer.Add(Unit("Program::A", first)));
        Assert.True(writer.Add(Unit("Program::B", second)));
        using var stream = new MemoryStream();
        writer.WriteTo(stream);
        return (stream.ToArray(), first, second);
    }

    private static UnitDependency Site(int offset) =>
        new(UnitQueryKind.TypeTestSite, $"Program::Run@{offset:x8}", new(7, (ulong)offset))
        {
            Value = new UnitLookup<TypeTestSiteModel>(true, new("Program::Run", offset, Matching[0], Matching)),
        };

    private static CompiledUnit Unit(string identity, UnitDependency site) =>
        new(identity, new(1, 2), [site], [0x00, 0x0B], [], 1) { FunctionName = identity };

    private static byte[] Encode(UnitDependency site) => UnitAnswerCodec.Encode(site.Kind, site.Key, site.Value)!;

    private static int Occurrences(byte[] bytes, ReadOnlySpan<byte> part)
    {
        var count = 0;
        for (var at = bytes.AsSpan().IndexOf(part); at >= 0;)
        {
            count++;
            var next = bytes.AsSpan(at + 1).IndexOf(part);
            at = next < 0 ? -1 : at + 1 + next;
        }
        return count;
    }

    private static byte[] Pinned(byte[] bytes)
    {
        var pinned = GC.AllocateUninitializedArray<byte>(bytes.Length, pinned: true);
        bytes.CopyTo(pinned, 0);
        return pinned;
    }
}
