using System.Text;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Wit.Bindings.Workers;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitWorkerClientCommandTests
{
    [Fact]
    public void GeneratesBothArtifactsThroughTheInjectedCapabilities()
    {
        var store = new MemoryFiles();
        var client = new RecordingClient();
        var options = new FixedOptions();
        var command = new WitWorkerClientCommand(options, store, client, store);
        Assert.Equal(0, command.Run([]));
        Assert.Equal(Path.GetFullPath("contract.json"), store.InputPath);
        var request = Assert.IsType<WitWorkerCSharpClientRequest>(client.Request);
        Assert.Equal(new byte[] { 1, 2 }, request.ContractJson.ToArray());
        Assert.Equal("Example.Workers", request.Namespace);
        Assert.Equal("Client", request.ClassName);
        Assert.Equal("example.worker", request.JavaScriptModule);
        Assert.Equal("./worker/client.mjs", request.WorkerClientModule);
        Assert.Equal("C# client", store.Outputs[Path.GetFullPath("client.cs")]);
        Assert.Equal("JS client", store.Outputs[Path.GetFullPath("client.mjs")]);
    }

    [Fact]
    public void TranslatesExpectedInputFailuresAndPreservesUnexpectedFailures()
    {
        foreach (var error in new Exception[] { new IOException("input"), new UnauthorizedAccessException("input"), new ArgumentException("input"), new NotSupportedException("input") })
        {
            var store = new MemoryFiles { Failure = error };
            var client = new RecordingClient();
            var command = new WitWorkerClientCommand(new FixedOptions(), store, client, store);
            var failure = Assert.Throws<WitBindingException>(() => command.Run([]));
            Assert.Contains("worker client generation failed", failure.Message, StringComparison.Ordinal);
            Assert.Empty(store.Outputs);
            Assert.Null(client.Request);
        }
        var unexpected = new InvalidOperationException("unexpected");
        var files = new MemoryFiles { Failure = unexpected };
        Assert.Same(unexpected, Assert.Throws<InvalidOperationException>(() =>
            new WitWorkerClientCommand(new FixedOptions(), files, new RecordingClient(), files).Run([])));
    }

    [Fact]
    public void RejectsMissingDependenciesAndComposesTheWorkerCommand()
    {
        var options = new FixedOptions(); var store = new MemoryFiles(); var client = new RecordingClient();
        Assert.Throws<ArgumentNullException>(() => new WitWorkerClientCommand(null!, store, client, store));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerClientCommand(options, null!, client, store));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerClientCommand(options, store, null!, store));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerClientCommand(options, store, client, null!));
        Assert.IsType<WitWorkerClientCommand>(WitWorkerClientCommandComposition.Create());
    }

    private sealed class FixedOptions : IWitWorkerClientOptionsReader
    {
        public WitWorkerClientOptions Read(IReadOnlyList<string> arguments) =>
            new("contract.json", "client.cs", "client.mjs", "Example.Workers", "Client", "example.worker", "./worker/client.mjs");
    }

    private sealed class MemoryFiles : IByteFileReader, ITextFileWriter
    {
        public Dictionary<string, string> Outputs { get; } = new(StringComparer.Ordinal);
        public string? InputPath { get; private set; }
        public Exception? Failure { get; init; }
        public byte[] Read(string path) { InputPath = path; if (Failure is not null) throw Failure; return [1, 2]; }
        public void Write(string path, string content) => Outputs.Add(path, content);
    }

    private sealed class RecordingClient : IWitWorkerCSharpClientWriter
    {
        public WitWorkerCSharpClientRequest? Request { get; private set; }
        public WitWorkerCSharpClientSource Write(WitWorkerCSharpClientRequest request)
        {
            Request = request;
            return new(Encoding.UTF8.GetBytes("C# client"), Encoding.UTF8.GetBytes("JS client"));
        }
    }
}
