using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class WitDocumentReaderTests
{
    [Fact]
    public void ReadsNormalizedWorldInterfacesFunctionsAndTypes()
    {
        var reader = new WitDocumentReader(new StubTools(
            "contract.wit", 0, NormalizedDocument, string.Empty));

        var document = reader.Read("contract.wit");
        var world = document.SelectWorld("example:smoke@1.0.0/smoke");

        Assert.Equal("example:smoke@1.0.0", world.Package);
        Assert.Equal("smoke", world.Name);
        Assert.Single(world.Imports);
        Assert.Equal(0, world.Imports[0].InterfaceId);
        Assert.Single(world.Exports);
        Assert.Equal("run", world.Exports[0].Function!.Name);
        Assert.Equal(new WitTypeReference.Primitive("u32"),
            world.Exports[0].Function!.Result);
        Assert.Equal("host", document.Interfaces[0].Name);
        Assert.Equal(new WitTypeReference.Defined(0),
            document.Interfaces[0].Functions[0].Result);
        Assert.Null(document.Interfaces[0].Functions[1].Result);
        Assert.Equal("result", document.Types[0].Kind.EnumerateObject().Single().Name);
    }

    [Fact]
    public void ReadsAnExistingNormalizedDocumentWithoutInvokingWasmTools()
    {
        var reader = new WitDocumentJsonReader();

        var document = reader.Read(NormalizedDocument);

        Assert.IsAssignableFrom<IWitDocumentJsonReader>(reader);
        Assert.Equal(NormalizedDocument, document.NormalizedJson);
        Assert.Equal("smoke", Assert.Single(document.Worlds).Name);
        Assert.Equal("host", Assert.Single(document.Interfaces).Name);
    }

    [Fact]
    public void DelegatesSuccessfulToolOutputToTheInjectedJsonReader()
    {
        var expected = new WitDocument([], [], [], [], "expected");
        var json = new RecordingJsonReader(expected);
        var reader = new WitDocumentReader(
            new StubTools("contract.wit", 0, "normalized", string.Empty),
            json);

        Assert.Same(expected, reader.Read("contract.wit"));
        Assert.Equal("normalized", json.Input);
    }

    [Fact]
    public void SelectWorldRequiresOneUnambiguousWorld()
    {
        var document = new WitDocument([], [], [], [], "{}");

        var exception = Assert.Throws<CompilerException>(() => document.SelectWorld(null));

        Assert.Equal("NW1009", exception.Diagnostic.Id);
        Assert.Contains("exactly one world", exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SelectWorldMatchesByNameAndRejectsMissingOrAmbiguousNames()
    {
        var selected = new WitWorld(0, "main", "example:test@1.0.0", [], []);
        var document = new WitDocument([], [], [selected], [], "{}");

        Assert.Equal(selected, document.SelectWorld("main"));
        Assert.Equal(selected, document.SelectWorld(null));

        var missing = Assert.Throws<CompilerException>(() =>
            document.SelectWorld("missing"));
        Assert.Contains("was not found or is ambiguous", missing.Diagnostic.Message,
            StringComparison.Ordinal);

        var ambiguous = new WitDocument(
            [],
            [],
            [selected, selected with { Id = 1, Package = "other:test@1.0.0" }],
            [],
            "{}");
        var duplicate = Assert.Throws<CompilerException>(() =>
            ambiguous.SelectWorld("main"));
        Assert.Contains("was not found or is ambiguous", duplicate.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidWitBecomesDeterministicComponentDiagnostic()
    {
        var reader = new WitDocumentReader(new StubTools(
            "bad.wit", 1, string.Empty, "bad\nsyntax"));

        var exception = Assert.Throws<CompilerException>(() => reader.Read("bad.wit"));

        Assert.Equal("NW1009", exception.Diagnostic.Id);
        Assert.Equal("invalid WIT input: bad syntax", exception.Diagnostic.Message);
    }

    [Fact]
    public void NormalizesWhitespaceOnlyToolErrors()
    {
        var reader = new WitDocumentReader(new StubTools(
            "whitespace.wit", 1, string.Empty, " \r\n"));

        var exception = Assert.Throws<CompilerException>(() => reader.Read(
            "whitespace.wit"));

        Assert.Contains("unknown error", exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsMalformedToolJsonAndUnsupportedWorldItems()
    {
        var malformed = new WitDocumentReader(new StubTools(
            "malformed.wit", 0, "not-json", string.Empty));
        var jsonException = Assert.Throws<CompilerException>(() => malformed.Read(
            "malformed.wit"));
        Assert.Contains("invalid WIT JSON", jsonException.Diagnostic.Message,
            StringComparison.Ordinal);

        var unsupported = new WitDocumentReader(new StubTools(
            "unsupported.wit", 0, UnsupportedWorldItemDocument, string.Empty));
        var itemException = Assert.Throws<CompilerException>(() => unsupported.Read(
            "unsupported.wit"));
        Assert.Contains("unsupported WIT world item", itemException.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsStructuredFunctionKindsAndTypeOwners()
    {
        var reader = new WitDocumentReader(new StubTools(
            "structured.wit", 0, StructuredDocument, string.Empty));

        var document = reader.Read("structured.wit");

        Assert.Equal("constructor", document.Interfaces[0].Functions[0].Kind.Name);
        Assert.Equal(0, document.Interfaces[0].Functions[0].Kind.ResourceType);
        Assert.Equal(0, document.Types[0].OwnerInterface);
        Assert.Null(document.Types[1].OwnerInterface);
    }

    private sealed class StubTools(
        string path,
        int exitCode,
        string output,
        string error) : IWasmTools
    {
        public ToolResult Run(params IEnumerable<string> arguments)
        {
            Assert.Equal(["component", "wit", path, "--json", "--no-docs"],
                arguments);
            return new ToolResult(exitCode, output, error);
        }
    }

    private sealed class RecordingJsonReader(WitDocument result) : IWitDocumentJsonReader
    {
        public string? Input { get; private set; }

        public WitDocument Read(string normalizedJson)
        {
            Input = normalizedJson;
            return result;
        }
    }

    private const string NormalizedDocument = """
        {
          "worlds": [{
            "name": "smoke",
            "imports": {"interface-0": {"interface": {"id": 0}}},
            "exports": {"run": {"function": {
              "name": "run", "kind": "freestanding",
              "params": [{"name": "value", "type": "u32"}], "result": "u32"
            }}},
            "package": 0
          }],
          "interfaces": [{
            "name": "host", "types": {},
            "functions": {"transform": {
              "name": "transform", "kind": "freestanding",
              "params": [{"name": "value", "type": "string"}], "result": 0
            }, "notify": {
              "name": "notify", "kind": "freestanding", "params": []
            }},
            "package": 0
          }],
          "types": [{
            "name": null,
            "kind": {"result": {"ok": "string", "err": "string"}},
            "owner": null
          }],
          "packages": [{
            "name": "example:smoke@1.0.0",
            "interfaces": {"host": 0}, "worlds": {"smoke": 0}
          }]
        }
        """;

    private const string UnsupportedWorldItemDocument = """
        {
          "worlds": [{"name":"main","package":0,"imports":{"bad":{"unknown":{}}},"exports":{}}],
          "interfaces": [], "types": [], "packages":[{"name":"example:test@1.0.0","interfaces":{},"worlds":{}}]
        }
        """;

    private const string StructuredDocument = """
        {
          "worlds": [], "interfaces": [{
            "name":"api", "package":0, "types":{},
            "functions":{"make":{"name":"make","kind":{"constructor":0},"params":[]}}
          }],
          "types":[
            {"name":"first","kind":"resource","owner":{"interface":0}},
            {"name":"second","kind":"resource","owner":{}}
          ],
          "packages":[{"name":"example:test@1.0.0","interfaces":{"api":0},"worlds":{}}]
        }
        """;
}
