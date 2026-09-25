using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class MalformedCompilationRunnerTests
{
    [Fact]
    public void MalformedResponseRetainsProcessAndArtifactIdentity()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-malformed-response-test-");
        try
        {
            var environment = CompilerCorrectnessEnvironment.Discover();
            var observation = new MalformedCompilationRunner(environment, new InvalidResponseProcess()).Compile(
                Mutation(), environment.CoreLibPath, "Entry", directory.FullName, 0);

            Assert.IsType<System.Text.Json.JsonException>(observation.ResponseFailure);
            Assert.Null(observation.Diagnostic);
            Assert.True(observation.Process!.Succeeded);
            Assert.Equal("{", File.ReadAllText(observation.ArtifactPrefix + ".response.json"));
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class InvalidResponseProcess : IQualifiedProcessRunner
    {
        public QualifiedProcessResult Run(QualifiedProcessRequest request, CancellationToken cancellationToken = default)
        {
            File.WriteAllText(request.Arguments[2], "{");
            return new(QualifiedProcessCompletion.Exited, 0, "", "", TimeSpan.Zero);
        }
    }
    [Fact]
    public void CapturesStructuredDiagnosticsWithoutCreatingAModule()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var runner = new MalformedCompilationRunner(
            environment,
            new CapturedDiagnosticProcessRunner());

        var observation = runner.Compile(
            Mutation(),
            environment.CoreLibPath,
            "Example.EntryPoint",
            CorrectnessTestAssets.CreateDirectory(),
            0);

        Assert.Equal(QualifiedProcessCompletion.Exited, observation.Completion);
        Assert.Equal(0, observation.ExitCode);
        Assert.Equal(DiagnosticCode.InvalidCil, observation.Diagnostic?.Code);
        Assert.Equal("Run", observation.Diagnostic?.Method);
        Assert.Equal(7, observation.Diagnostic?.IlOffset);
        Assert.False(observation.ModuleExists);
    }

    [Fact]
    public void ReportsACompilerChildTimeoutAsABoundedObservation()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var runner = new MalformedCompilationRunner(
            environment,
            new TimedOutProcessRunner());

        var observation = runner.Compile(
            Mutation(),
            environment.CoreLibPath,
            "Example.EntryPoint",
            CorrectnessTestAssets.CreateDirectory(),
            0);

        Assert.Equal(QualifiedProcessCompletion.TimedOut, observation.Completion);
        Assert.Null(observation.ExitCode);
        Assert.Null(observation.Diagnostic);
        Assert.False(observation.ModuleExists);
    }

    private static MalformedInputMutation Mutation() => new(
        "invalid",
        "single-invariant",
        "input.dll",
        "sha",
        0,
        "00",
        "ff");

    [Theory]
    [InlineData("invalid")]
    [InlineData("../name-is-not-a-path")]
    [InlineData("/name-is-not-an-absolute-path")]
    public void RepeatedCaseAndAttemptNeverConsumeThePreviousInvocationArtifacts(string name)
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-rejection-isolation-test-");
        try
        {
            var environment = CompilerCorrectnessEnvironment.Discover();
            var processes = new FirstInvocationArtifacts();
            var runner = new MalformedCompilationRunner(environment, processes);
            var mutation = Mutation() with { Name = name };
            var first = runner.Compile(mutation, environment.CoreLibPath, "Entry", directory.FullName, 0);
            var second = runner.Compile(mutation, environment.CoreLibPath, "Entry", directory.FullName, 0);

            Assert.NotNull(first.Diagnostic);
            Assert.True(first.ModuleExists);
            Assert.True(first.FailureSnapshotExists);
            Assert.Null(second.Diagnostic);
            Assert.False(second.ModuleExists);
            Assert.False(second.FailureSnapshotExists);
            Assert.Equal(2, processes.Requests.Count);
            Assert.NotEqual(processes.Requests[0], processes.Requests[1]);
            Assert.Equal(first.ArtifactPrefix + ".request.json", processes.Requests[0]);
            Assert.Equal(second.ArtifactPrefix + ".request.json", processes.Requests[1]);
            Assert.All(processes.Requests, path => Assert.True(File.Exists(path)));
            Assert.All(processes.Requests, path => Assert.Equal(directory.FullName, Path.GetDirectoryName(path)));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InvalidRequestFailsBeforeDirectoryCreationOrChildExecution(int defect)
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-rejection-validation-test-");
        try
        {
            var environment = CompilerCorrectnessEnvironment.Discover();
            var processes = new FirstInvocationArtifacts();
            var output = Path.Combine(directory.FullName, "uncreated");
            var runner = new MalformedCompilationRunner(environment, processes);

            Assert.ThrowsAny<ArgumentException>(() => runner.Compile(
                defect == 0 ? null! : Mutation(), defect == 1 ? " " : environment.CoreLibPath,
                defect == 2 ? "" : "Entry", defect == 3 ? null! : output, defect == 4 ? -1 : 0));

            Assert.Empty(processes.Requests);
            Assert.False(Directory.Exists(output));
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class FirstInvocationArtifacts : IQualifiedProcessRunner
    {
        public List<string> Requests { get; } = [];

        public QualifiedProcessResult Run(QualifiedProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request.Arguments[1]);
            if (Requests.Count == 1)
            {
                File.WriteAllText(request.Arguments[2], """{"Code":1002,"Message":"first","Method":"Run","IlOffset":7}""");
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(request.Arguments[1]));
                File.WriteAllBytes(json.RootElement.GetProperty("ModulePath").GetString()!, [1]);
                var passes = json.RootElement.GetProperty("DiagnosticTracePath").GetString()! + ".passes";
                Directory.CreateDirectory(passes);
                File.WriteAllText(Path.Combine(passes, "failure.json"), "{}");
            }
            return new(QualifiedProcessCompletion.Exited, 0, "", "", TimeSpan.Zero);
        }
    }

    private sealed class CapturedDiagnosticProcessRunner : IQualifiedProcessRunner
    {
        public QualifiedProcessResult Run(
            QualifiedProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            File.WriteAllText(
                request.Arguments[2],
                """
                {"Code":1002,"Message":"invalid","Method":"Run","IlOffset":7}
                """);
            return new(
                QualifiedProcessCompletion.Exited,
                0,
                "",
                "",
                TimeSpan.Zero);
        }
    }

    private sealed class TimedOutProcessRunner : IQualifiedProcessRunner
    {
        public QualifiedProcessResult Run(
            QualifiedProcessRequest request,
            CancellationToken cancellationToken = default) => new(
            QualifiedProcessCompletion.TimedOut,
            null,
            "",
            "",
            request.Timeout);
    }
}
