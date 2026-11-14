using NetWasm.Compiler.Core;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class DiagnosticReachableMethodAnalyzerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureUsesImmutableRequestPathUnlessDeeperChainExists(bool existingChain)
    {
        var root = new CompilerDependencyStep(new(new(new("App"), 1), "App.Root"), CompilerDependencyKind.MethodDependency);
        var caller = new CompilerDependencyStep(new(new(new("Library"), 2), "Library.Caller"), CompilerDependencyKind.MethodDependency);
        var inner = new CompilerDependencyStep(new(new(new("Inner"), 3), "Inner.Source"), CompilerDependencyKind.Interface);
        var diagnostic = new CompilerDiagnostic(DiagnosticCode.AssemblyResolution, "Missing operation")
        {
            Context = caller.Source,
            DependencyChain = existingChain ? [inner] : [],
        };
        var failure = new CompilerException(diagnostic);
        var analyzer = new DiagnosticReachableMethodAnalyzer(new ReachableMethodAnalyzerProbe(failure),
            NullLogger<DiagnosticReachableMethodAnalyzer>.Instance, new CompilerDiagnosticProvenanceBinder());
        var request = new ReachableMethodRequest(ValidationTestData.Instance())
        {
            DependencyPath = ImmutableStack<CompilerDependencyStep>.Empty.Push(root).Push(caller),
        };

        var observed = Assert.Throws<CompilerException>(() => ((IReachableMethodAnalyzer)analyzer).Analyze(request));

        Assert.Same(diagnostic.Context, observed.Diagnostic.Context);
        Assert.Equal(existingChain ? [inner] : new[] { root, caller }, observed.Diagnostic.DependencyChain);
        if (existingChain) Assert.Same(failure, observed);
        else Assert.Same(failure, observed.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnalyzePreservesFailurePayloadAndTheMostSpecificSource(bool existingContext)
    {
        var context = new CompilerDiagnosticContext(
            new EntityKey(new AssemblyIdentity("Inner.Library"), 0x06000002), "Inner.ClosedMethod");
        var diagnostic = new CompilerDiagnostic(DiagnosticCode.AssemblyResolution,
            "A required dependency is unavailable.", "External.Entry", 12)
        {
            Context = existingContext ? context : null,
        };
        var failure = new CompilerException(diagnostic);
        var logger = new RecordingLogger();
        var analyzer = new DiagnosticReachableMethodAnalyzer(new ReachableMethodAnalyzerProbe(failure), logger, new CompilerDiagnosticProvenanceBinder());
        var method = ValidationTestData.Instance() with
        {
            MethodArguments = [CliTypeIdentity.Primitive("i4", CliValueKind.I4)],
        };
        var request = new ReachableMethodRequest(method);

        var observed = Assert.Throws<CompilerException>(() => analyzer.Analyze(request));

        Assert.Equal(diagnostic.Code, observed.Diagnostic.Code);
        Assert.Equal(diagnostic.Message, observed.Diagnostic.Message);
        Assert.Equal(diagnostic.Method, observed.Diagnostic.Method);
        Assert.Equal(diagnostic.IlOffset, observed.Diagnostic.IlOffset);
        Assert.Equal(failure.Message, observed.Message);
        Assert.Same(failure, Assert.Single(logger.Entries).Exception);
        if (existingContext)
        {
            Assert.Same(failure, observed);
            Assert.Same(context, observed.Diagnostic.Context);
        }
        else
        {
            Assert.Same(failure, observed.InnerException);
            Assert.Equal(new CompilerDiagnosticContext(method.Definition.Key, method.CanonicalName),
                observed.Diagnostic.Context);
            Assert.Null(diagnostic.Context);
        }
    }

    [Fact]
    public void AnalyzeDelegatesTheRequestAndReturnsTheResult()
    {
        var inner = new ReachableMethodAnalyzerProbe();
        var analyzer = new DiagnosticReachableMethodAnalyzer(
            inner,
            NullLogger<DiagnosticReachableMethodAnalyzer>.Instance, new CompilerDiagnosticProvenanceBinder());
        var request = new ReachableMethodRequest(ValidationTestData.Instance());

        var result = ((IReachableMethodAnalyzer)analyzer).Analyze(request);

        Assert.Null(result);
        Assert.Same(request, inner.Request);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public void AnalyzeLogsTheMethodAndRethrowsTheOriginalFailure()
    {
        var failure = new InvalidOperationException();
        var inner = new ReachableMethodAnalyzerProbe(failure);
        var logger = new RecordingLogger();
        var analyzer = new DiagnosticReachableMethodAnalyzer(inner, logger, new CompilerDiagnosticProvenanceBinder());
        var request = new ReachableMethodRequest(ValidationTestData.Instance());

        var observed = Assert.Throws<InvalidOperationException>(() =>
            ((IReachableMethodAnalyzer)analyzer).Analyze(request));

        Assert.Same(failure, observed);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(failure, entry.Exception);
        Assert.Contains(request.Method.ToString(), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorAndAnalyzeRejectNullInputs()
    {
        var inner = new ReachableMethodAnalyzerProbe();
        var logger = NullLogger<DiagnosticReachableMethodAnalyzer>.Instance;
        var analyzer = new DiagnosticReachableMethodAnalyzer(inner, logger, new CompilerDiagnosticProvenanceBinder());

        Assert.Throws<ArgumentNullException>(() =>
            new DiagnosticReachableMethodAnalyzer(null!, logger, new CompilerDiagnosticProvenanceBinder()));
        Assert.Throws<ArgumentNullException>(() =>
            new DiagnosticReachableMethodAnalyzer(inner, null!, new CompilerDiagnosticProvenanceBinder()));
        Assert.Throws<ArgumentNullException>(() =>
            new DiagnosticReachableMethodAnalyzer(inner, logger, null!));
        Assert.Throws<ArgumentNullException>(() =>
            ((IReachableMethodAnalyzer)analyzer).Analyze(null!));
    }

    private sealed class ReachableMethodAnalyzerProbe(Exception? failure = null) :
        IReachableMethodAnalyzer
    {
        internal int CallCount { get; private set; }
        internal ReachableMethodRequest? Request { get; private set; }

        public ReachableMethodAnalysis Analyze(ReachableMethodRequest request)
        {
            CallCount++;
            Request = request;
            if (failure is not null)
            {
                throw failure;
            }

            return null!;
        }
    }

    private sealed class RecordingLogger : ILogger<DiagnosticReachableMethodAnalyzer>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
    }

    private sealed record LogEntry(
        LogLevel Level,
        Exception? Exception,
        string Message);
}
