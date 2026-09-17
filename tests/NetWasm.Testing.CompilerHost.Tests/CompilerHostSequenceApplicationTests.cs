using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Testing.CompilerHost;

namespace NetWasm.Testing.CompilerHost.Tests;

public sealed class CompilerHostSequenceApplicationTests
{
    [Fact]
    public void RunsStepsInOneHostAndWritesTimingAndArtifactIdentity()
    {
        using var files = new SequenceTestFiles();
        var first = Request(files.PathFor("first.wasm"));
        var second = Request(files.PathFor("second.wasm"));
        var runner = new RecordingRequestRunner();
        var receiptPath = files.PathFor("receipt.json");

        var exitCode = new CompilerHostSequenceApplication(runner).Run(new([
            new("cold", first, files.PathFor("first.json")),
            new("identical", second, files.PathFor("second.json")),
        ]), receiptPath);

        Assert.Equal(0, exitCode);
        Assert.Equal([first, second], runner.Requests);
        var receipt = JsonSerializer.Deserialize<CompilationSequenceReceipt>(
            File.ReadAllText(receiptPath));
        Assert.NotNull(receipt);
        Assert.Equal(2, receipt.SchemaVersion);
        Assert.Equal(["cold", "identical"], receipt.Steps.Select(step => step.Label));
        Assert.All(receipt.Steps, step =>
        {
            Assert.True(step.ElapsedMilliseconds >= 0);
            Assert.Equal(0, step.ExitCode);
            Assert.Equal(CompilerMetricsOutcome.Succeeded, step.Outcome);
            Assert.True(step.AdapterDuration >= TimeSpan.Zero);
            Assert.Equal(step.AdapterDuration, step.CompilerTiming?.TotalDuration);
            Assert.Equal(
                step.AdapterDuration - step.CompilerMetrics?.TotalDuration,
                step.CompilerTiming?.ResidualDuration);
            Assert.NotNull(step.ModuleSha256);
            Assert.Equal(64, step.ModuleSha256.Length);
            Assert.Equal(CompilerMetricsOutcome.Succeeded, step.CompilerMetrics?.Outcome);
        });
        Assert.NotEqual(receipt.Steps[0].ModuleSha256, receipt.Steps[1].ModuleSha256);
    }

    [Fact]
    public void StopsAtTheFirstFailedStepAndPublishesTheFailureReceipt()
    {
        using var files = new SequenceTestFiles();
        var runner = new RecordingRequestRunner { FailureCall = 2 };
        var receiptPath = files.PathFor("receipt.json");

        var exitCode = new CompilerHostSequenceApplication(runner).Run(new([
            new("first", Request(files.PathFor("first.wasm")), files.PathFor("first.json")),
            new("failure", Request(files.PathFor("failure.wasm")), files.PathFor("failure.json")),
            new("unreached", Request(files.PathFor("unreached.wasm")), files.PathFor("unreached.json")),
        ]), receiptPath);

        Assert.Equal(7, exitCode);
        Assert.Equal(2, runner.Requests.Count);
        var receipt = JsonSerializer.Deserialize<CompilationSequenceReceipt>(
            File.ReadAllText(receiptPath));
        Assert.NotNull(receipt);
        Assert.Equal(2, receipt.SchemaVersion);
        Assert.Equal(2, receipt.Steps.Length);
        Assert.Equal(7, receipt.Steps[1].ExitCode);
        Assert.Equal(CompilerMetricsOutcome.Failed, receipt.Steps[1].Outcome);
        Assert.Null(receipt.Steps[1].ModuleSha256);
        Assert.Equal(CompilerMetricsOutcome.Failed, receipt.Steps[1].CompilerMetrics?.Outcome);
    }

    [Fact]
    public void RejectsAnEmptySuccessfulCompilerResponse()
    {
        using var files = new SequenceTestFiles();
        var runner = new RecordingRequestRunner { EmptyResponseCall = 1 };

        Assert.Throws<InvalidOperationException>(() =>
            new CompilerHostSequenceApplication(runner).Run(new([
                new("empty", Request(files.PathFor("module.wasm")), files.PathFor("response.json")),
            ]), files.PathFor("receipt.json")));
    }

    [Fact]
    public void RejectsAnEmptyFailureResponse()
    {
        using var files = new SequenceTestFiles();
        var runner = new RecordingRequestRunner
        {
            FailureCall = 1,
            EmptyResponseCall = 1,
        };

        Assert.Throws<InvalidOperationException>(() =>
            new CompilerHostSequenceApplication(runner).Run(new([
                new("failure", Request(files.PathFor("module.wasm")), files.PathFor("response.json")),
            ]), files.PathFor("receipt.json")));
    }

    [Fact]
    public void RejectsMissingCapabilitiesAndInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CompilerHostSequenceApplication(null!));
        var application = new CompilerHostSequenceApplication(
            new RecordingRequestRunner());
        Assert.Throws<ArgumentNullException>(() => application.Run(null!, "receipt.json"));
        Assert.Throws<ArgumentException>(() => application.Run(new([]), ""));
        Assert.Throws<ArgumentException>(() => application.Run(new([
            new("", Request("module.wasm"), "response.json"),
        ]), "receipt.json"));
    }

    private static CompilationRequest Request(string modulePath) => new(
        "entry.dll",
        [],
        "Application",
        "Run",
        [],
        WasmTarget.Wasm32,
        null,
        [],
        ImmutableDictionary<string, string>.Empty,
        modulePath);

    private sealed class RecordingRequestRunner : ICompilerHostRequestRunner
    {
        public List<CompilationRequest> Requests { get; } = [];
        public int FailureCall { get; init; }
        public int EmptyResponseCall { get; init; }

        public int Run(CompilationRequest request, string responsePath)
        {
            Requests.Add(request);
            if (Requests.Count == FailureCall)
            {
                File.WriteAllText(responsePath, Requests.Count == EmptyResponseCall
                    ? "null"
                    : JsonSerializer.Serialize(
                    new FailedCompilationResponse(
                        CompilerMetricsOutcome.Failed,
                        TimeSpan.FromMilliseconds(2),
                        new(TimeSpan.FromMilliseconds(2), TimeSpan.Zero, TimeSpan.FromMilliseconds(2)),
                        Metrics(CompilerMetricsOutcome.Failed))));
                return 7;
            }
            File.WriteAllBytes(request.ModulePath, [(byte)Requests.Count]);
            File.WriteAllText(responsePath, Requests.Count == EmptyResponseCall
                ? "null"
                : JsonSerializer.Serialize(new CompilationResponse(
                ImmutableDictionary<int, string>.Empty,
                "hash",
                0,
                TimeSpan.FromMilliseconds(1),
                new(TimeSpan.FromMilliseconds(1), TimeSpan.Zero, TimeSpan.FromMilliseconds(1)),
                Metrics(CompilerMetricsOutcome.Succeeded))));
            return 0;
        }

        private static CompilerMetricsReport Metrics(CompilerMetricsOutcome outcome) => new(
            outcome,
            outcome == CompilerMetricsOutcome.Succeeded ? null : "analysis",
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            []);
    }

    private sealed class SequenceTestFiles : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), $"compiler-sequence-{Guid.NewGuid():N}");

        public SequenceTestFiles() => Directory.CreateDirectory(_root);
        public string PathFor(string name) => Path.Combine(_root, name);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
