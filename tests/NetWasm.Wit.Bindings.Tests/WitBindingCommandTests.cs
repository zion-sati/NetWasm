using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Wit.Bindings;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitBindingCommandTests
{
    [Fact]
    public void GeneratesSelectedWorldAndWritesSource()
    {
        var document = Document();
        var files = new RecordingTextFiles();
#pragma warning disable CA1859 // Contract tests deliberately dispatch through the one-action interface.
        IWitBindingCommand command = new WitBindingCommand(
            new FixedOptionsReader(new("contract.wit", null, "Bindings.g.cs")),
            new FixedDocumentReader(document),
            new FixedGenerator("generated source"),
            new WitBindingSourceAccessibilityRewriter(),
            files);
#pragma warning restore CA1859

        var result = command.Run([]);

        Assert.Equal(0, result);
        Assert.Equal("Bindings.g.cs", files.Path);
        Assert.Equal("generated source", files.Content);
    }

    [Fact]
    public void RequiresEveryCapability()
    {
        var options = new FixedOptionsReader(new("contract.wit", null, "Bindings.g.cs"));
        var documents = new FixedDocumentReader(Document());
        var bindings = new FixedGenerator("source");
        var accessibility = new WitBindingSourceAccessibilityRewriter();
        var files = new RecordingTextFiles();

        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingCommand(null!, documents, bindings, accessibility, files));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingCommand(options, null!, bindings, accessibility, files));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingCommand(options, documents, null!, accessibility, files));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingCommand(options, documents, bindings, null!, files));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingCommand(options, documents, bindings, accessibility, null!));
    }

    [Fact]
    public void AppliesSelectedTopLevelAccessibility()
    {
        var files = new RecordingTextFiles();
        var command = new WitBindingCommand(
            new FixedOptionsReader(new(
                "contract.wit",
                null,
                "Bindings.g.cs",
                WitBindingAccessibility.Internal)),
            new FixedDocumentReader(Document()),
            new FixedGenerator("public sealed class Resource\n{\n    public void Dispose() { }\n}\n"),
            new WitBindingSourceAccessibilityRewriter(),
            files);

        _ = command.Run([]);

        Assert.Equal(
            "internal sealed class Resource\n{\n    public void Dispose() { }\n}\n",
            files.Content);
    }

    private static WitDocument Document()
    {
        var world = new WitWorld(
            0,
            "main",
            "example:test@1.0.0",
            ImmutableArray<WitWorldItem>.Empty,
            ImmutableArray<WitWorldItem>.Empty);
        return new WitDocument(
            [new WitPackage(0, world.Package,
                ImmutableDictionary<string, int>.Empty,
                ImmutableDictionary<string, int>.Empty.Add("main", 0))],
            [],
            [world],
            [],
            "{}");
    }

    private sealed class FixedOptionsReader(WitBindingOptions options) :
        IWitBindingOptionsReader
    {
        public WitBindingOptions Read(string[] arguments) => options;
    }

    private sealed class FixedDocumentReader(WitDocument document) : IWitDocumentReader
    {
        public WitDocument Read(string witPath) => document;
    }

    private sealed class FixedGenerator(string source) : IWitCSharpBindingGenerator
    {
        public string Generate(WitDocument document, WitWorld world) => source;
    }

    private sealed class RecordingTextFiles : ITextFileWriter
    {
        public string? Path { get; private set; }

        public string? Content { get; private set; }

        public void Write(string path, string content)
        {
            Path = path;
            Content = content;
        }
    }
}
