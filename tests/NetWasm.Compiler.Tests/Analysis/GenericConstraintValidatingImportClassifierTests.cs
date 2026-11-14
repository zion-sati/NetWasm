using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.GenericConstraints;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class GenericConstraintValidatingImportClassifierTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidatesBeforeClassificationAndStopsOnFailure(bool reject)
    {
        var calls = new List<string>();
        var constraints = new Constraints(calls, reject);
        var inner = new Inner(calls);
        var classifier = Assert.IsAssignableFrom<IReachabilityImportClassifier>(
            new GenericConstraintValidatingImportClassifier(inner, constraints));
        var request = new ReachabilityImportRequest(ValidationTestData.Instance());
        if (reject)
        {
            Assert.Throws<CompilerException>(() => classifier.Classify(request));
            Assert.Equal(["validate"], calls);
            Assert.Null(inner.Request);
        }
        else
        {
            Assert.Same(inner.Result, classifier.Classify(request));
            Assert.Equal(["validate", "classify"], calls);
            Assert.Same(request, inner.Request);
        }
        Assert.Same(request.Method, constraints.Method);
        Assert.Throws<ArgumentNullException>(() => classifier.Classify(null!));
        Assert.Throws<ArgumentNullException>(() => new GenericConstraintValidatingImportClassifier(null!, constraints));
        Assert.Throws<ArgumentNullException>(() => new GenericConstraintValidatingImportClassifier(inner, null!));
    }

    private sealed class Constraints(List<string> calls, bool reject) : IClosedGenericConstraintValidator
    {
        public MethodInstanceModel? Method { get; private set; }
        public void Validate(MethodInstanceModel method)
        {
            Method = method; calls.Add("validate");
            if (reject) throw new CompilerException(new(DiagnosticCode.AssemblyResolution, "Constraint failure"));
        }
        public void Validate(CliTypeIdentity type) => throw new InvalidOperationException();
    }
    private sealed class Inner(List<string> calls) : IReachabilityImportClassifier
    {
        public ReachabilityImportAnalysis Result { get; } = new(false, [], [], [], [], [], [], null, null, [], null);
        public ReachabilityImportRequest? Request { get; private set; }
        public ReachabilityImportAnalysis Classify(ReachabilityImportRequest request)
        {
            calls.Add("classify"); Request = request; return Result;
        }
    }
}
