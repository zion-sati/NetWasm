using System.Collections.Immutable;
using NetWasm.Compiler.Browser.Wit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Browser.Tests.Wit;

public sealed class VirtualWitDocumentReaderTests
{
    [Fact]
    public void ReadsTheActiveSessionRequest()
    {
        var expected = new WitDocument([], [], [], [], "JSON");
        var resolver = FixedBrowserCompilationRequestResolver.ForDocuments(
            ImmutableDictionary<string, string>.Empty.Add("contract", "JSON"));
        var reader = new VirtualWitDocumentReader(resolver, new RecordingJsonReader(expected));

        Assert.Same(expected, reader.Read("contract"));
    }

    [Fact]
    public void DelegatesTheSelectedNormalizedDocumentWithoutToolExecution()
    {
        var expected = new WitDocument([], [], [], [], "JSON");
        var json = new RecordingJsonReader(expected);
        var reader = CreateReader(ImmutableDictionary<string, string>.Empty
            .Add("first.wit.wasm", "first JSON").Add("second.wit.wasm", "second JSON"), json);

        Assert.Same(expected, reader.Read("second.wit.wasm"));
        Assert.Equal("second JSON", Assert.Single(json.Inputs));
    }

    [Fact]
    public void ReadsNormalizedWorldIdentity()
    {
        var reader = CreateReader(ImmutableDictionary<string, string>.Empty.Add("contract.wit.wasm", """
            {"packages":[{"name":"example:test@1.0.0","interfaces":{},"worlds":{"main":0}}],
             "interfaces":[],"types":[],
             "worlds":[{"name":"main","package":0,"imports":{},"exports":{}}]}
            """), new WitDocumentJsonReader());

        var world = reader.Read("contract.wit.wasm").SelectWorld("example:test@1.0.0/main");

        Assert.Equal("main", world.Name);
        Assert.Equal("example:test@1.0.0", world.Package);
    }

    [Fact]
    public void AppliesTheSuppliedCoreBindingInventoryForNamedWorldItems()
    {
        const string path = "contract.wit.wasm";
        var documents = ImmutableDictionary<string, string>.Empty.Add(path, """
            {"packages":[{"name":"example:test@1.0.0","interfaces":{"api":0},"worlds":{"main":0}}],
             "interfaces":[{"name":"api","package":0,"types":{},"functions":{"run":{"name":"run","kind":"freestanding","params":[]}}}],
             "types":[],
             "worlds":[{"name":"main","package":0,"imports":{},"exports":{"interface-0":{"interface":{"id":0}}}}]}
            """);
        var inventories = ImmutableDictionary<string, string>.Empty.Add(path, """
            (module
              (export "cm32p2|interface-0|run" (func 0))
              (export "cm32p2|interface-0|run_post" (func 1))
            )
            """);
        var reader = new VirtualWitDocumentReader(
            documents,
            inventories,
            new WitDocumentJsonReader());

        var resolved = reader.Read(path, "main");

        Assert.Equal("interface-0",
            Assert.Single(resolved.SelectWorld("main").Exports).CoreBindingName);
    }

    [Fact]
    public void RejectsInterfacePlacementsWithoutACoreBindingInventory()
    {
        const string path = "contract.wit.wasm";
        var reader = CreateReader(
            ImmutableDictionary<string, string>.Empty.Add(path, """
                {"packages":[{"name":"example:test@1.0.0","interfaces":{"api":0},"worlds":{"main":0}}],
                 "interfaces":[{"name":"api","package":0,"types":{},"functions":{"run":{"name":"run","kind":"freestanding","params":[]}}}],
                 "types":[],
                 "worlds":[{"name":"main","package":0,"imports":{},"exports":{"interface-0":{"interface":{"id":0}}}}]}
                """),
            new WitDocumentJsonReader());

        var failure = Assert.Throws<CompilerException>(() =>
            reader.Read(path, "main"));

        Assert.Contains("requires a core binding inventory", failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RootOnlyWorldDoesNotRequireACoreBindingInventory()
    {
        const string path = "contract.wit.wasm";
        var reader = CreateReader(
            ImmutableDictionary<string, string>.Empty.Add(path, """
                {"packages":[{"name":"example:test@1.0.0","interfaces":{},"worlds":{"main":0}}],
                 "interfaces":[],"types":[],
                 "worlds":[{"name":"main","package":0,"imports":{"run":{"function":{"name":"run","kind":"freestanding","params":[]}}},"exports":{}}]}
                """),
            new WitDocumentJsonReader());

        var resolved = reader.Read(path, "main");

        Assert.Equal("run", Assert.Single(resolved.SelectWorld("main").Imports).Name);
    }

    [Fact]
    public void PreservesTheJsonReadersCompilerDiagnostic()
    {
        var expected = new CompilerException(new CompilerDiagnostic(DiagnosticCode.ComponentContract, "invalid virtual WIT"));
        var json = new FailingJsonReader(expected);
        var reader = CreateReader(ImmutableDictionary<string, string>.Empty.Add("contract", "JSON"), json);

        var actual = Assert.Throws<CompilerException>(() => reader.Read("contract"));

        Assert.Same(expected, actual);
        Assert.Same(expected.Diagnostic, actual.Diagnostic);
    }

    [Fact]
    public void MissingInputsDoNotInvokeTheJsonReader()
    {
        var json = new RecordingJsonReader(new WitDocument([], [], [], [], ""));
        var reader = CreateReader(ImmutableDictionary<string, string>.Empty.Add("folder/contract", "JSON"), json);

        var failure = Assert.Throws<FileNotFoundException>(() => reader.Read("contract"));

        Assert.Equal("contract", failure.FileName);
        Assert.Empty(json.Inputs);
    }

    [Fact]
    public void RequiresItsDependenciesAndValidPaths()
    {
        Assert.Throws<ArgumentNullException>(() => CreateReader(null!, new WitDocumentJsonReader()));
        Assert.Throws<ArgumentNullException>(() => CreateReader(ImmutableDictionary<string, string>.Empty, null!));
        Assert.Throws<ArgumentNullException>(() => new VirtualWitDocumentReader(
            (IBrowserCompilationRequestResolver)null!, new WitDocumentJsonReader()));
        Assert.Throws<ArgumentNullException>(() => new VirtualWitDocumentReader(
            FixedBrowserCompilationRequestResolver.ForDocuments(
                ImmutableDictionary<string, string>.Empty), null!));
        var reader = CreateReader(ImmutableDictionary<string, string>.Empty, new WitDocumentJsonReader());
        Assert.Throws<ArgumentNullException>(() => reader.Read(null!));
        Assert.Throws<ArgumentException>(() => reader.Read(" "));
    }

    private static IWitDocumentReader CreateReader(
        ImmutableDictionary<string, string> documents,
        IWitDocumentJsonReader json) =>
        Assert.IsAssignableFrom<IWitDocumentReader>(new VirtualWitDocumentReader(documents, json));

    private sealed class RecordingJsonReader(WitDocument result) : IWitDocumentJsonReader
    {
        public List<string> Inputs { get; } = [];

        public WitDocument Read(string normalizedJson)
        {
            Inputs.Add(normalizedJson);
            return result;
        }
    }

    private sealed class FailingJsonReader(CompilerException failure) : IWitDocumentJsonReader
    {
        public WitDocument Read(string normalizedJson) => throw failure;
    }
}
