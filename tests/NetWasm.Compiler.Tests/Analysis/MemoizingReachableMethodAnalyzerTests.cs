using System;
using System.Collections.Generic;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using Xunit;

namespace NetWasm.Compiler.Tests.Analysis;

// A reducing closure builds the program again after each reduction; a method no reduction
// rewrote analyzes as it did, so its analysis is taken from the compile's memo.
public sealed class MemoizingReachableMethodAnalyzerTests
{
    private static readonly AssemblyIdentity Assembly = new("fixture");

    [Fact]
    public void AMethodAnalyzesOnceForEachBodyItIsGiven()
    {
        var calls = new List<ReachableMethodRequest>();
        var memo = new ReachableMethodAnalysisMemo();
        var first = new MemoizingReachableMethodAnalyzer(new Recording(calls), memo);
        var second = new MemoizingReachableMethodAnalyzer(new Recording(calls), memo);
        var method = Method("Run", 1);
        var rewritten = Body(method);

        var original = first.Analyze(new(method));
        // Another round's request for the same method, on another worker.
        Assert.Same(original, second.Analyze(new(Method("Run", 1))));
        var reduced = first.Analyze(new(method) { RewrittenBody = rewritten });
        Assert.Same(reduced, second.Analyze(new(method) { RewrittenBody = rewritten }));
        // A body is the reduction's own object: an equal one from another reduction is analyzed.
        Assert.NotSame(reduced, first.Analyze(new(method) { RewrittenBody = Body(method) }));
        Assert.NotSame(original, first.Analyze(new(Method("Other", 2))));

        Assert.Equal(4, calls.Count);
        Assert.Equal(4, memo.Count);
    }

    [Fact]
    public void AnAnalysisAnotherWorkerStoredFirstIsTheOneEveryWorkerTakes()
    {
        var memo = new ReachableMethodAnalysisMemo();
        var request = new ReachableMethodRequest(Method("Run", 1));
        ReachableMethodAnalysis? stored = null;

        // Both workers miss; the second to finish takes the first's analysis.
        var taken = memo.GetOrAdd(request, () =>
        {
            stored = memo.GetOrAdd(request, () => Analysis(request));
            return Analysis(request);
        });

        Assert.Same(stored, taken);
    }

    [Fact]
    public void RequiresItsCollaboratorsAndARequest()
    {
        var memo = new ReachableMethodAnalysisMemo();
        Assert.Throws<ArgumentNullException>(() => new MemoizingReachableMethodAnalyzer(null!, memo));
        Assert.Throws<ArgumentNullException>(() => new MemoizingReachableMethodAnalyzer(new Recording([]), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new MemoizingReachableMethodAnalyzer(new Recording([]), memo).Analyze(null!));
    }

    private static ReachableMethodAnalysis Analysis(ReachableMethodRequest request) =>
        new(request.Method, null!, [], [], null!);

    private static MethodInstanceModel Method(string name, int token)
    {
        var signature = MethodSignatureModel.Create(CliTypeIdentity.Primitive("void", CliValueKind.Void));
        var definition = new MethodDefinitionModel(new(Assembly, 0x06000000 + token), new(Assembly, 0x02000002), name,
            true, signature, 0);
        return new(definition, CliTypeIdentity.Named(Assembly, "Fixture", "Program", isValueType: false), [], signature);
    }

    private static CilMethodBody Body(MethodInstanceModel method) => new(method.Definition, 1, [], []);

    private sealed class Recording(List<ReachableMethodRequest> calls) : IReachableMethodAnalyzer
    {
        public ReachableMethodAnalysis Analyze(ReachableMethodRequest request)
        {
            calls.Add(request);
            return Analysis(request);
        }
    }
}
