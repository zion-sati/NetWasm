using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class DiagnosticReachabilityImportClassifierTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AddsMissingProvenanceAndPreservesDeeperDetails(bool contextExists, bool chainExists)
    {
        var source = new CompilerDiagnosticContext(new(new("Inner"), 1), "Inner.Method");
        var step = new CompilerDependencyStep(source, CompilerDependencyKind.MethodDependency);
        var diagnostic = new CompilerDiagnostic(DiagnosticCode.NativeInterop, "Unsupported import", "Import", 5)
        {
            Context = contextExists ? source : null,
            DependencyChain = chainExists ? [step] : [],
        };
        var failure = new CompilerException(diagnostic);
        var caller = new CompilerDependencyStep(new(new(new("App"), 2), "App.Entry"), CompilerDependencyKind.MethodDependency);
        var request = new ReachabilityImportRequest(ValidationTestData.Instance())
        {
            DependencyPath = ImmutableStack<CompilerDependencyStep>.Empty.Push(caller),
        };
        var classifier = Assert.IsAssignableFrom<IReachabilityImportClassifier>(
            new DiagnosticReachabilityImportClassifier(new Probe(failure), new CompilerDiagnosticProvenanceBinder()));

        var actual = Assert.Throws<CompilerException>(() => classifier.Classify(request));

        Assert.Equal(diagnostic.Code, actual.Diagnostic.Code);
        Assert.Equal(diagnostic.Message, actual.Diagnostic.Message);
        Assert.Equal(diagnostic.Method, actual.Diagnostic.Method);
        Assert.Equal(diagnostic.IlOffset, actual.Diagnostic.IlOffset);
        Assert.Equal(contextExists ? source : new(request.Method.Definition.Key, request.Method.CanonicalName),
            actual.Diagnostic.Context);
        Assert.Equal(chainExists ? step : caller, Assert.Single(actual.Diagnostic.DependencyChain));
        if (contextExists && chainExists) Assert.Same(failure, actual);
        else Assert.Same(failure, actual.InnerException);
    }

    [Fact]
    public void DelegatesSuccessfulRequestsAndDoesNotReclassifyUnrelatedExceptions()
    {
        var probe = new Probe();
        var classifier = Assert.IsAssignableFrom<IReachabilityImportClassifier>(new DiagnosticReachabilityImportClassifier(probe, new CompilerDiagnosticProvenanceBinder()));
        var request = new ReachabilityImportRequest(ValidationTestData.Instance());
        Assert.Same(probe.Result, classifier.Classify(request));
        Assert.Same(request, probe.Request);
        var failure = new InvalidOperationException();
        var failing = Assert.IsAssignableFrom<IReachabilityImportClassifier>(new DiagnosticReachabilityImportClassifier(new Probe(failure), new CompilerDiagnosticProvenanceBinder()));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => failing.Classify(request)));
        Assert.Throws<ArgumentNullException>(() => classifier.Classify(null!));
        Assert.Throws<ArgumentNullException>(() => new DiagnosticReachabilityImportClassifier(null!, new CompilerDiagnosticProvenanceBinder()));
        Assert.Throws<ArgumentNullException>(() => new DiagnosticReachabilityImportClassifier(probe, null!));
    }

    private sealed class Probe(Exception? failure = null) : IReachabilityImportClassifier
    {
        public ReachabilityImportRequest? Request { get; private set; }
        public ReachabilityImportAnalysis Result { get; } = new(false, [], [], [], [], [], [], null, null, [], null);
        public ReachabilityImportAnalysis Classify(ReachabilityImportRequest request)
        {
            Request = request;
            if (failure is not null) throw failure;
            return Result;
        }
    }
}
