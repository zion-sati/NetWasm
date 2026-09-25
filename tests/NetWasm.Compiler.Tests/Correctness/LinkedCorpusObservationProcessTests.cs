using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LinkedCorpusObservationProcessTests
{
    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void ObservePreservesRequestCommandCancellationAndResponse(string target)
    {
        var dependencies = new Dependencies();
        var request = LinkedCorpusObservationResponseParserTests.CreateRequest() with { Target = target };
        var typeNames = ImmutableDictionary<int, string>.Empty.Add(17, "System.Exception");
        using var cancellation = new CancellationTokenSource();

        var result = CreateProcess(dependencies).Observe(request, typeNames, Environment, cancellation.Token);

        Assert.Same(dependencies.Response, result);
        Assert.Equal(["write", "run", "read"], dependencies.Calls);
        Assert.Equal("module.wasm.observation.request.json", dependencies.RequestPath);
        Assert.Same(request, dependencies.WrittenRequest);
        Assert.Equal(cancellation.Token, dependencies.Cancellation);
        Assert.Equal("node", dependencies.ProcessRequest!.FileName);
        Assert.Equal(["runner.mjs", "module.wasm.observation.request.json", "module.wasm.observation.response.json"],
            dependencies.ProcessRequest.Arguments.AsEnumerable());
        Assert.Equal(TimeSpan.FromSeconds(3), dependencies.ProcessRequest.Timeout);
        Assert.Equal("module.wasm.observation.response.json", dependencies.ResponsePath);
        Assert.Same(request, dependencies.ReadRequest);
        Assert.Same(typeNames, dependencies.TypeNames);
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(2, 1, null)]
    [InlineData(3, 2, null)]
    public void ObserveRetainsFailedProcessWithoutReadingOrRetry(
        int marker, int completion, int? exitCode)
    {
        var dependencies = new Dependencies
        {
            ProcessResult = new((QualifiedProcessCompletion)completion, exitCode,
                marker.ToString(System.Globalization.CultureInfo.InvariantCulture), "", TimeSpan.Zero)
            {
                LaunchException = new InvalidOperationException("launch"),
            },
        };

        var failure = Assert.Throws<LinkedCorpusObservationException>(() =>
            CreateProcess(dependencies).Observe(
                LinkedCorpusObservationResponseParserTests.CreateRequest(),
                ImmutableDictionary<int, string>.Empty, Environment));

        Assert.Same(dependencies.ProcessResult, failure.Result);
        Assert.Same(dependencies.ProcessResult.LaunchException, failure.InnerException);
        Assert.Equal(["write", "run"], dependencies.Calls);
        Assert.Same(dependencies.WrittenRequest, failure.Invocation.Request);
        Assert.Same(dependencies.ProcessRequest, failure.Invocation.Process);
        Assert.Equal(dependencies.RequestPath, failure.Invocation.RequestPath);
        Assert.Equal("module.wasm.observation.response.json", failure.Invocation.ResponsePath);
    }

    [Theory]
    [InlineData("write", 1)]
    [InlineData("run", 2)]
    [InlineData("read", 3)]
    public void ObservePreservesFirstCollaboratorFailure(string stage, int callCount)
    {
        var dependencies = new Dependencies { FailureStage = stage };
        var failure = Assert.Throws<InvalidOperationException>(() =>
            CreateProcess(dependencies).Observe(
                LinkedCorpusObservationResponseParserTests.CreateRequest(),
                ImmutableDictionary<int, string>.Empty, Environment));
        Assert.Same(dependencies.Failure, failure);
        Assert.Equal(callCount, dependencies.Calls.Count);
        Assert.Equal(stage, dependencies.Calls[^1]);
        Assert.True(dependencies.Calls.Count(call => call == "run") <= 1);
    }

    [Fact]
    public void ObserveRejectsInvalidInputsAndCancellationBeforeWriting()
    {
        var dependencies = new Dependencies();
        var process = CreateProcess(dependencies);
        var request = LinkedCorpusObservationResponseParserTests.CreateRequest();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<ArgumentNullException>(() => process.Observe(null!, ImmutableDictionary<int, string>.Empty, Environment));
        Assert.Throws<ArgumentNullException>(() => process.Observe(request, null!, Environment));
        Assert.Throws<ArgumentNullException>(() => process.Observe(request, ImmutableDictionary<int, string>.Empty, null!));
        var failure = Assert.Throws<OperationCanceledException>(() =>
            process.Observe(request, ImmutableDictionary<int, string>.Empty, Environment, cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Empty(dependencies.Calls);
    }

    private static ILinkedCorpusObservationProcess CreateProcess(Dependencies dependencies) =>
        Assert.IsAssignableFrom<ILinkedCorpusObservationProcess>(new LinkedCorpusObservationProcess(
            dependencies, dependencies, dependencies));

    private static LinkedCorpusObservationEnvironment Environment { get; } =
        new("node", "runner.mjs", TimeSpan.FromSeconds(3));

    private sealed class Dependencies : ILinkedCorpusObservationRequestWriter,
        IQualifiedProcessRunner, ILinkedCorpusObservationResponseReader
    {
        public List<string> Calls { get; } = [];
        public string? FailureStage { get; init; }
        public InvalidOperationException Failure { get; } = new("sentinel");
        public LinkedCorpusObservationResult Response { get; } = new(
            ImmutableDictionary<int, OracleObservation>.Empty, "module", "manifest");
        public QualifiedProcessResult ProcessResult { get; init; } =
            new(QualifiedProcessCompletion.Exited, 0, "", "", TimeSpan.Zero);
        public string? RequestPath { get; private set; }
        public LinkedCorpusObservationRequest? WrittenRequest { get; private set; }
        public QualifiedProcessRequest? ProcessRequest { get; private set; }
        public CancellationToken Cancellation { get; private set; }
        public string? ResponsePath { get; private set; }
        public LinkedCorpusObservationRequest? ReadRequest { get; private set; }
        public ImmutableDictionary<int, string>? TypeNames { get; private set; }

        public void Write(string path, LinkedCorpusObservationRequest request)
        {
            Record("write");
            RequestPath = path;
            WrittenRequest = request;
        }

        public QualifiedProcessResult Run(QualifiedProcessRequest request, CancellationToken cancellationToken = default)
        {
            Record("run");
            ProcessRequest = request;
            Cancellation = cancellationToken;
            return ProcessResult;
        }

        public LinkedCorpusObservationResult Read(
            string path,
            LinkedCorpusObservationRequest request,
            ImmutableDictionary<int, string> typeNames)
        {
            Record("read");
            ResponsePath = path;
            ReadRequest = request;
            TypeNames = typeNames;
            return Response;
        }

        private void Record(string stage)
        {
            Calls.Add(stage);
            if (stage == FailureStage) throw Failure;
        }
    }
}
