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
            new WitInputStager(TextReader.Null),
            new FixedDocumentReader(document),
            new FixedGenerator("generated source"),
            new WitBindingSourceAccessibilityRewriter(),
            files,
            TextWriter.Null);
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
        var inputs = new WitInputStager(TextReader.Null);
        var output = TextWriter.Null;

        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingCommand(null!, inputs, documents, bindings, accessibility, files, output));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingCommand(options, null!, documents, bindings, accessibility, files, output));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingCommand(options, inputs, null!, bindings, accessibility, files, output));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingCommand(options, inputs, documents, null!, accessibility, files, output));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingCommand(options, inputs, documents, bindings, null!, files, output));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingCommand(options, inputs, documents, bindings, accessibility, null!, output));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingCommand(options, inputs, documents, bindings, accessibility, files, null!));
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
            new WitInputStager(TextReader.Null),
            new FixedDocumentReader(Document()),
            new FixedGenerator("public sealed class Resource\n{\n    public void Dispose() { }\n}\n"),
            new WitBindingSourceAccessibilityRewriter(),
            files,
            TextWriter.Null);

        _ = command.Run([]);

        Assert.Equal(
            "internal sealed class Resource\n{\n    public void Dispose() { }\n}\n",
            files.Content);
    }

    // `--output -` writes the source to standard output instead of a file.
    [Fact]
    public void WritesSourceToStandardOutput()
    {
        var files = new RecordingTextFiles();
        using var output = new StringWriter();
        var command = new WitBindingCommand(
            new FixedOptionsReader(new("contract.wit", null, "-")),
            new WitInputStager(TextReader.Null),
            new FixedDocumentReader(Document()),
            new FixedGenerator("generated source"),
            new WitBindingSourceAccessibilityRewriter(),
            files,
            output);

        _ = command.Run([]);

        Assert.Equal("generated source", output.ToString());
        Assert.Null(files.Path);
    }

    // `--wit -` reads the WIT from standard input; wasm-tools reads it from a
    // temporary copy, which is removed afterwards.
    [Fact]
    public void ReadsWitFromStandardInput()
    {
        const string wit = "package example:test@1.0.0;\n\nworld main {}\n";
        var documents = new FixedDocumentReader(Document());
        var command = new WitBindingCommand(
            new FixedOptionsReader(new("-", null, "Bindings.g.cs")),
            new WitInputStager(new StringReader(wit)),
            documents,
            new FixedGenerator("generated source"),
            new WitBindingSourceAccessibilityRewriter(),
            new RecordingTextFiles(),
            TextWriter.Null);

        _ = command.Run([]);

        Assert.Equal(wit, documents.Text);
        Assert.False(File.Exists(documents.Path));
    }

    [Fact]
    public void PassesTheSelectedNamespaceToTheGenerator()
    {
        var bindings = new FixedGenerator("generated source");
        var command = new WitBindingCommand(
            new FixedOptionsReader(new("contract.wit", null, "Bindings.g.cs", Namespace: "My.Bindings")),
            new WitInputStager(TextReader.Null),
            new FixedDocumentReader(Document()),
            bindings,
            new WitBindingSourceAccessibilityRewriter(),
            new RecordingTextFiles(),
            TextWriter.Null);

        _ = command.Run([]);

        Assert.Equal("My.Bindings", bindings.Namespace);
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
        public string? Path { get; private set; }

        public string? Text { get; private set; }

        public WitDocument Read(string witPath)
        {
            Path = witPath;
            Text = File.Exists(witPath) ? File.ReadAllText(witPath) : null;
            return document;
        }
    }

    private sealed class FixedGenerator(string source) : IWitCSharpBindingGenerator
    {
        public string? Namespace { get; private set; }

        public string Generate(WitDocument document, WitWorld world, string? namespaceName = null)
        {
            Namespace = namespaceName;
            return source;
        }
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
