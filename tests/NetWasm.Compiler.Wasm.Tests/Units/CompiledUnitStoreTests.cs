using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

public sealed class CompiledUnitStoreTests
{
    private static CompiledUnit Unit(string identity, UnitFingerprint stringAnswer) =>
        new(identity,
            new(1, 2),
            [new(UnitQueryKind.StringLayout, "hello", stringAnswer), new(UnitQueryKind.StringTypeId, "", new(5, 6))],
            [0x00, 0x10, 0xAC, 0x02, 0x0B],
            [new(2, 2, WasmIndexSpace.Function, "method:Program::Helper", false)],
            3)
        {
            FunctionName = identity.Replace("::", "_", StringComparison.Ordinal),
            Parameters = [NetWasm.Compiler.Core.CliValueKind.I4],
            Result = NetWasm.Compiler.Core.CliValueKind.I8,
            IsNoInlining = true,
            EnvironmentKey = "env",
            BlockEmissionCounts = ImmutableDictionary<int, int>.Empty.Add(0, 1).Add(4, 2),
        };

    private static UnitStoreChange Change(UnitSegmentWriter writer, int sequence = 1)
    {
        var entries = writer.Entries.Select(entry => new UnitManifestEntry(entry.Hash, 0, entry.Offset)).ToArray();
        return new([new(sequence, writer)],
            UnitManifestFormat.Write([new(sequence, writer.Count, writer.Length)], entries, sequence + 1),
            new HashSet<int> { sequence }, "summary");
    }

    [Fact]
    public void AUnitIsReadInPlaceAsItWasWritten()
    {
        var unit = Unit("Program::Run", new(3, 4));
        var writer = new UnitSegmentWriter();
        Assert.True(writer.Add(unit));
        var memory = new UnitStoreMemory();
        new MemoryUnitStoreCommitter(memory).Commit("app", Change(writer));

        using var store = new MemoryUnitStoreLoader(memory).Load("app");

        var location = Assert.IsType<UnitLocation>(store.Find("Program::Run"));
        var segment = store.Segments[location.Segment]!;
        var restored = segment.Record(location.Offset).ToUnit();
        Assert.Equal(unit.Identity, restored.Identity);
        Assert.Equal(unit.Input, restored.Input);
        Assert.Equal(unit.Dependencies, restored.Dependencies);
        Assert.Equal(unit.Body, restored.Body);
        Assert.Equal(unit.Relocations, restored.Relocations);
        Assert.Equal(unit.InstructionCount, restored.InstructionCount);
        Assert.Equal(unit.FunctionName, restored.FunctionName);
        Assert.Equal(unit.Parameters, restored.Parameters);
        Assert.Equal(unit.Result, restored.Result);
        Assert.Equal(unit.IsNoInlining, restored.IsNoInlining);
        Assert.Equal(unit.EnvironmentKey, restored.EnvironmentKey);
        Assert.Equal(unit.BlockEmissionCounts, restored.BlockEmissionCounts);
        Assert.Null(store.Find("Program::Other"));
    }

    // A unit a segment cannot hold is declined as full, so another segment can take it; one it
    // already holds or whose answer disagrees is refused (issue 229).
    [Fact]
    public void ASegmentDeclinesAUnitAsFullOnlyWhenItDoesNotFit()
    {
        Assert.False(new UnitSegmentWriter(sectionLimit: 1).Add(Unit("Program::A", new(3, 4)), out var full));
        Assert.True(full);
        var writer = new UnitSegmentWriter();
        Assert.True(writer.Add(Unit("Program::A", new(3, 4)), out full));
        Assert.False(full);
        Assert.False(writer.Add(Unit("Program::A", new(3, 4)), out full));
        Assert.False(full);
        Assert.False(writer.Add(Unit("Program::B", new(9, 9)), out full));
        Assert.False(full);
    }

    [Fact]
    public void ASegmentStoresEachQueryOnceAndRefusesADisagreeingAnswer()
    {
        var writer = new UnitSegmentWriter();
        Assert.True(writer.Add(Unit("Program::A", new(3, 4))));
        Assert.True(writer.Add(Unit("Program::B", new(3, 4))));
        Assert.False(writer.Add(Unit("Program::C", new(9, 9))));
        Assert.False(writer.Add(Unit("Program::A", new(3, 4))));
        var memory = new UnitStoreMemory();
        new MemoryUnitStoreCommitter(memory).Commit("app", Change(writer));

        using var store = new MemoryUnitStoreLoader(memory).Load("app");

        var segment = store.Segments[0]!;
        Assert.Equal(2, segment.QueryCount);
        Assert.Equal(2, store.Count);
        Assert.Null(store.Find("Program::C"));
    }

    [Fact]
    public void AManifestOfAnotherVersionReadsAsNoUnits()
    {
        var writer = new UnitSegmentWriter();
        writer.Add(Unit("Program::Run", new(3, 4)));
        var change = Change(writer);
        change.Manifest![4] = 4;
        var memory = new UnitStoreMemory();
        new MemoryUnitStoreCommitter(memory).Commit("app", change);

        using var store = new MemoryUnitStoreLoader(memory).Load("app");

        Assert.Equal(0, store.Count);
        Assert.Null(store.Find("Program::Run"));
    }

    [Fact]
    public void MappedFilesReadTheCommittedUnitsAndDeadSegmentsAreDeleted()
    {
        var location = Path.Combine(Path.GetTempPath(), "netwasm-units-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new UnitSegmentWriter();
            first.Add(Unit("Program::Run", new(3, 4)));
            new FileUnitStoreCommitter(new UnitFileDeleter()).Commit(location, Change(first, 1));
            var second = new UnitSegmentWriter();
            second.Add(Unit("Program::Next", new(3, 4)));
            new FileUnitStoreCommitter(new UnitFileDeleter()).Commit(location, Change(second, 2));

            using (var store = new MappedFileUnitStoreLoader(new NoUnitMemoryPrefetcher()).Load(location))
            {
                var found = Assert.IsType<UnitLocation>(store.Find("Program::Next"));
                Assert.Equal("Program_Next", store.Segments[found.Segment]!.Record(found.Offset).FunctionName.ToString());
                Assert.Null(store.Find("Program::Run"));
            }
            var directory = UnitStoreFiles.Directory(location);
            Assert.False(File.Exists(UnitStoreFiles.Segment(directory, 1)));
            Assert.True(File.Exists(UnitStoreFiles.Segment(directory, 2)));
            Assert.Equal("summary", File.ReadAllText(Path.Combine(directory, "last-compile.txt")));
        }
        finally
        {
            if (Directory.Exists(location)) Directory.Delete(location, recursive: true);
        }
    }

    [Fact]
    public void ATruncatedSegmentLeavesItsUnitsUnfound()
    {
        var writer = new UnitSegmentWriter();
        writer.Add(Unit("Program::Run", new(3, 4)));
        var change = Change(writer);
        var memory = new UnitStoreMemory();
        new MemoryUnitStoreCommitter(memory).Commit("app", change);
        var segment = memory.Segment("app", 1)!;
        var truncated = GC.AllocateUninitializedArray<byte>(segment.Length - 8, pinned: true);
        segment.AsSpan(0, truncated.Length).CopyTo(truncated);

        using var store = UnitStore.Over(change.Manifest!, _ => new PinnedUnitSegmentMemory(truncated));

        Assert.Null(store.Find("Program::Run"));
    }
}
