using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.Artifacts;

namespace NetWasm.Compiler.Tasks.Tests.ComponentModel;

public sealed class ComponentManifestInputsReaderTests
{
    [Fact]
    public void ReadsTargetMatchedInteropBoundaryAndAdapterVersions()
    {
        using var file = new TemporaryJsonFile(ValidManifest("wasm64"));
        var reader = CreateReader();

        var result = reader.Read(new(file.Path, "wasm64", "1.28.1", "0.20.1"));

        var import = Assert.Single(result.JavaScript.Imports);
        Assert.Equal("host", import.Module);
        Assert.Equal("read", import.Name);
        Assert.Equal("task", import.AsyncReturn);
        var export = Assert.Single(result.JavaScript.Exports);
        Assert.Equal("run", export.Name);
        Assert.Equal("value-task", export.AsyncReturn);
        Assert.Equal("1.28.1", result.Adapters.Jco);
        Assert.Equal("0.20.1", result.Adapters.Preview2Shim);
        var witImport = Assert.Single(result.WitImports);
        Assert.Equal("wasi:http@0.2.11/outgoing-handler", witImport.Interface);
        Assert.Equal("handle", witImport.Function);
    }

    [Fact]
    public void RejectsNullEmptyAndMismatchedInputs()
    {
        var reader = CreateReader();
        Assert.Throws<ArgumentNullException>(() => reader.Read(null!));
        using var empty = new TemporaryJsonFile("null");
        Assert.Throws<InvalidOperationException>(() =>
            reader.Read(new(empty.Path, "wasm32", null, null)));
        using var mismatch = new TemporaryJsonFile(ValidManifest("wasm32"));
        Assert.Throws<InvalidOperationException>(() =>
            reader.Read(new(mismatch.Path, "wasm64", null, null)));
        Assert.Throws<ArgumentNullException>(() => new ComponentManifestInputsReader(null!));
    }

    private static string ValidManifest(string target) => $$"""
        {
          "version": 1,
          "target": "{{target}}",
          "statusAbi": { "successStatus": 0, "hostFailureStatus": 1, "scalarResultOffset": 0 },
          "targetLayout": {
            "managedReferenceSize": 8,
            "stringLengthOffset": 0,
            "stringDataOffset": 8,
            "arrayLengthOffset": 0,
            "arrayDataPointerOffset": 8
          },
          "imports": [
            {
              "module": "host",
              "name": "read",
              "parameters": ["i32"],
              "result": "i32",
              "asyncReturn": "task"
            }
          ],
          "exports": [
            {
              "name": "run",
              "parameters": [],
              "result": "void",
              "asyncReturn": "value-task"
            }
          ],
          "witImports": [
            {
              "interface": "wasi:http@0.2.11/outgoing-handler",
              "function": "handle"
            }
          ]
        }
        """;

    private static IComponentManifestInputsReader AsReader(object reader) =>
        (IComponentManifestInputsReader)reader;

    private static IComponentManifestInputsReader CreateReader() =>
        AsReader(new ComponentManifestInputsReader(new HostInteropManifestReader()));

    private sealed class TemporaryJsonFile : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"netwasm-component-inputs-{Guid.NewGuid():N}");

        public TemporaryJsonFile(string contents)
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "interop.json");
            File.WriteAllText(Path, contents);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
