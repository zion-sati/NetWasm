using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusApplicationCompilerTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompilePreservesRequestsProgressCancellationAndVerificationOrder(WasmTarget target)
    {
        var dependencies = new Dependencies();
        using var cancellation = new CancellationTokenSource();
        var compilation = CreateCompilation();
        var request = CreateRequest(target);
        var compiler = CreateCompiler(dependencies);

        var result = compiler.Compile(compilation, request, cancellation.Token);

        Assert.Same(dependencies.Response, result);
        Assert.Equal(["write", "run", "progress", "verify", "read"], dependencies.Calls);
        Assert.Equal("module.request.json", dependencies.WrittenPath);
        Assert.Same(request, dependencies.WrittenRequest);
        Assert.Equal(cancellation.Token, dependencies.Cancellation);
        var invocation = Assert.IsType<CorpusCompilerInvocation>(dependencies.Invocation);
        Assert.Same(compilation, invocation.Compilation);
        Assert.Equal(target, invocation.Target);
        Assert.Equal("dotnet", invocation.Request.FileName);
        Assert.Equal(["compiler.dll", "module.request.json", "module.response.json"], invocation.Request.Arguments.AsEnumerable());
        Assert.Equal(TimeSpan.FromSeconds(7), invocation.Request.Timeout);
        Assert.Equal("module.request.json", invocation.RequestPath);
        Assert.Equal("module.response.json", invocation.ResponsePath);
        Assert.Equal("module", invocation.ModulePath);
        Assert.Equal("trace", invocation.TracePath);
        Assert.Equal(request.ReferencePaths, invocation.ReferencePaths);
        Assert.Equal(request.SourcePaths, invocation.SourcePaths);
        Assert.Equal(("Fixture", target, 2, 5), dependencies.CompilerProgress);
        Assert.Same(dependencies.ProcessResult, dependencies.VerifiedResult);
        Assert.Same(invocation, dependencies.ReadInvocation);
    }

    [Theory]
    [InlineData("write", 1)]
    [InlineData("run", 2)]
    [InlineData("progress", 3)]
    [InlineData("verify", 4)]
    [InlineData("read", 5)]
    public void CompilePreservesFirstFailureWithoutRetryOrLaterCalls(string failureStage, int callCount)
    {
        var dependencies = new Dependencies { FailureStage = failureStage };

        var failure = Assert.Throws<InvalidOperationException>(() =>
            CreateCompiler(dependencies).Compile(CreateCompilation(), CreateRequest(WasmTarget.Wasm64)));

        Assert.Same(dependencies.Failure, failure);
        Assert.Equal(callCount, dependencies.Calls.Count);
        Assert.Equal(failureStage, dependencies.Calls[^1]);
        Assert.True(dependencies.Calls.Count(call => call == "run") <= 1);
    }

    [Fact]
    public void CompileRejectsInvalidInputsAndCancellationBeforeWriting()
    {
        var dependencies = new Dependencies();
        var compiler = CreateCompiler(dependencies);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<ArgumentNullException>(() => compiler.Compile(null!, CreateRequest(WasmTarget.Wasm32)));
        Assert.Throws<ArgumentNullException>(() => compiler.Compile(CreateCompilation(), null!));
        var failure = Assert.Throws<OperationCanceledException>(() =>
            compiler.Compile(CreateCompilation(), CreateRequest(WasmTarget.Wasm32), cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Empty(dependencies.Calls);
    }

    private static ICorpusApplicationCompiler CreateCompiler(Dependencies dependencies) =>
        Assert.IsAssignableFrom<ICorpusApplicationCompiler>(new CorpusApplicationCompiler(
            new("repository", "dotnet", "sdk", "roslyn", "corelib", "references", "node", "oracle", "compiler.dll", TimeSpan.FromSeconds(7)),
            dependencies, dependencies, dependencies, dependencies, dependencies));

    private static CorpusCompilation CreateCompilation()
    {
        var artifact = new CorpusArtifact("same.dll", "same.pdb", "hash", "pdb-hash", "compiler", []);
        return new(new("Fixture", "Tests", "", [0]), CilProfile.Release, artifact, artifact, "run");
    }

    internal static CorpusCompilerRequest CreateRequest(WasmTarget target) =>
        new("same.dll", ["corelib", "library"], "Tests.EntryPoint", "Run", [], target,
            "trace", ["first.cs", "second.cs"], ImmutableDictionary<string, string>.Empty,
            null, null, "module", false, null, "layout", "interop");

    private sealed class Dependencies : ICorpusCompilerRequestWriter, IQualifiedProcessRunner,
        ICorpusCompilerProcessVerifier, ICorpusCompilerResponseReader, IOracleOperationProgressReporter
    {
        public List<string> Calls { get; } = [];
        public string? FailureStage { get; init; }
        public InvalidOperationException Failure { get; } = new("sentinel");
        public CorpusCompilerResponse Response { get; } = new(ImmutableDictionary<int, string>.Empty, "module-hash", 123);
        public QualifiedProcessResult ProcessResult { get; } = new(QualifiedProcessCompletion.Exited, 0, "", "", TimeSpan.Zero);
        public string? WrittenPath { get; private set; }
        public CorpusCompilerRequest? WrittenRequest { get; private set; }
        public CancellationToken Cancellation { get; private set; }
        public CorpusCompilerInvocation? Invocation { get; private set; }
        public CorpusCompilerInvocation? ReadInvocation { get; private set; }
        public QualifiedProcessResult? VerifiedResult { get; private set; }
        public (string, WasmTarget, int, int) CompilerProgress { get; private set; }

        public void Write(string path, CorpusCompilerRequest request)
        {
            Record("write");
            WrittenPath = path;
            WrittenRequest = request;
        }

        public QualifiedProcessResult Run(QualifiedProcessRequest request, CancellationToken cancellationToken = default)
        {
            Record("run");
            Cancellation = cancellationToken;
            Assert.NotNull(request.Progress);
            request.Progress(2, 5);
            return ProcessResult;
        }

        public void Verify(CorpusCompilerInvocation invocation, QualifiedProcessResult result)
        {
            Record("verify");
            Invocation = invocation;
            VerifiedResult = result;
        }

        public CorpusCompilerResponse Read(CorpusCompilerInvocation invocation, QualifiedProcessResult result)
        {
            Record("read");
            ReadInvocation = invocation;
            Assert.Same(ProcessResult, result);
            return Response;
        }

        public void ReportCompilerPhases(string fixtureId, WasmTarget target, int completed, int total)
        {
            Record("progress");
            CompilerProgress = (fixtureId, target, completed, total);
        }

        public void Report(string fixtureId, WasmTarget target, OracleOperationStage stage, OracleOperationPlan operationPlan) => throw new NotSupportedException();
        public void ReportExecutionInputs(string fixtureId, WasmTarget target, OracleOperationStage stage, int completed, int total) => throw new NotSupportedException();
        public void ReportBatchAttempt(string fixtureId, WasmTarget target, OracleOperationStage stage, int attempt, int observed, int total, int batchSize) => throw new NotSupportedException();

        private void Record(string stage)
        {
            Calls.Add(stage);
            if (stage == FailureStage) throw Failure;
        }
    }
}
