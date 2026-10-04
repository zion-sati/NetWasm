using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class LinkerSymbolTraceReaderTests
{
    [Fact]
    public void ReadsPinnedLldEventsInOriginalOrder()
    {
        var reader = Assert.IsAssignableFrom<ILinkerSymbolTraceReader>(new LinkerSymbolTraceReader());
        var trace = "/native/libmule.a(first.o): lazy definition of sum\r\n" +
            "/native/libmule.a(first.o): reference to sum\n" +
            "/native/libmule.a(first.o): definition of sum\n" +
            "/output/runtime.wasm.lto.o: definition of sum\n";

        Assert.Equal([
            new RuntimeLinkerSymbolEvent("/native/libmule.a(first.o)", RuntimeLinkerSymbolEventKind.LazyDefinition, "sum"),
            new RuntimeLinkerSymbolEvent("/native/libmule.a(first.o)", RuntimeLinkerSymbolEventKind.Reference, "sum"),
            new RuntimeLinkerSymbolEvent("/native/libmule.a(first.o)", RuntimeLinkerSymbolEventKind.Definition, "sum"),
            new RuntimeLinkerSymbolEvent("/output/runtime.wasm.lto.o", RuntimeLinkerSymbolEventKind.Definition, "sum")],
            reader.Read(trace).ToArray());
    }

    [Fact]
    public void UsesTheLastMarkerWhenAnInputPathContainsTraceWords()
    {
        var trace = "/path: definition of directory/lib.a(member.o): definition of entry";
        var result = Assert.Single(new LinkerSymbolTraceReader().Read(trace));
        Assert.Equal("/path: definition of directory/lib.a(member.o)", result.InputIdentity);
        Assert.Equal("entry", result.EntryPoint);
    }

    [Fact]
    public void EmptyTraceHasNoEvents() =>
        Assert.Empty(new LinkerSymbolTraceReader().Read(string.Empty));

    [Theory]
    [InlineData("")]
    [InlineData("unrecognized trace output")]
    [InlineData(": definition of symbol")]
    [InlineData("input: definition of ")]
    [InlineData("input: lazy definition symbol")]
    [InlineData("input: definition of symbol\0suffix")]
    public void RejectsUnknownOrAmbiguousRecords(string line)
    {
        var trace = line.Length == 0 ? "\n" : line;
        Assert.Throws<InvalidOperationException>(() => new LinkerSymbolTraceReader().Read(trace));
    }

    [Fact]
    public void RejectsNullTrace() =>
        Assert.Throws<ArgumentNullException>(() => new LinkerSymbolTraceReader().Read(null!));
}
