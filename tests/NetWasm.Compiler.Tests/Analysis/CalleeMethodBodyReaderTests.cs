using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Tests.Analysis.Constants;

namespace NetWasm.Compiler.Tests.Analysis;

public sealed class CalleeMethodBodyReaderTests
{
    private static readonly MethodInstanceModel Callee =
        ConstantComparisonEvaluatorTests.StringMethod("Callee", isStatic: true);

    private static readonly CilMethodBody Body = new(Callee.Definition, 1, [],
        [new(0, 1, CilOperation.Return, new CilOperand.None())]);

    [Fact]
    public void DecodableBodiesAreRead()
    {
        var reader = new CalleeMethodBodyReader(new Bodies(failure: null));

        Assert.True(reader.TryReadMethodBody(Callee, out var instanceBody));
        Assert.Same(Body, instanceBody);
        Assert.True(reader.TryReadMethodBody(Callee.Definition, out var definitionBody));
        Assert.Same(Body, definitionBody);
    }

    // A body that references something missing is the callee's own failure, reported when the
    // compiler analyzes the callee; the look-ahead only learns that the body is unavailable.
    [Fact]
    public void UndecodableBodiesAreUnavailable()
    {
        var failure = new CompilerException(new(DiagnosticCode.MissingApi, "missing member"));
        var reader = new CalleeMethodBodyReader(new Bodies(failure));

        Assert.False(reader.TryReadMethodBody(Callee, out var instanceBody));
        Assert.Null(instanceBody);
        Assert.False(reader.TryReadMethodBody(Callee.Definition, out var definitionBody));
        Assert.Null(definitionBody);
    }

    [Fact]
    public void OtherFailuresPropagate()
    {
        var reader = new CalleeMethodBodyReader(new Bodies(new InvalidOperationException()));

        Assert.Throws<InvalidOperationException>(() => reader.TryReadMethodBody(Callee, out _));
    }

    [Fact]
    public void RequiresABodyReader() =>
        Assert.Throws<ArgumentNullException>(() => new CalleeMethodBodyReader(null!));

    private sealed class Bodies(Exception? failure) : IMethodBodyReader
    {
        public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => Read();

        public CilMethodBody ReadMethodBody(MethodInstanceModel method) => Read();

        private CilMethodBody Read() => failure is null ? Body : throw failure;
    }
}
