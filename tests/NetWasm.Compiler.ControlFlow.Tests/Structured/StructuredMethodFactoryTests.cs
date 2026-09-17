using NetWasm.Compiler.Core;
using New = global::NetWasm.Compiler.ControlFlow.Structured;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structured;

public sealed class StructuredMethodFactoryTests
{
    [Fact]
    public void CreateValidatesExactlyOnceBeforePublishingTheMethod()
    {
        var construction = Construction();
        var validator = new RecordingValidator();
        var factory = new New.StructuredMethodFactory(validator);

        var method = ((New.IStructuredMethodFactory)factory).Create(construction);

        Assert.Equal(1, validator.CallCount);
        Assert.Same(method, validator.Method);
        Assert.Equal(construction.Header, method.Header);
        Assert.Equal(construction.Blocks, method.Blocks);
        Assert.Equal(construction.Body, method.Body);
    }

    [Fact]
    public void CreateDoesNotPublishAMethodRejectedByValidation()
    {
        var expected = new InvalidOperationException("invalid structure");
        var validator = new RecordingValidator { Exception = expected };
        var factory = new New.StructuredMethodFactory(validator);

        var actual = Assert.Throws<New.StructuredMethodValidationException>(() =>
            ((New.IStructuredMethodFactory)factory).Create(Construction()));

        Assert.Same(expected, actual.InnerException);
        Assert.Equal(1, validator.CallCount);
    }

    [Fact]
    public void CreateRequiresItsValidatorAndConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => new New.StructuredMethodFactory(null!));
        var factory = new New.StructuredMethodFactory(new RecordingValidator());
        Assert.Throws<ArgumentNullException>(() =>
            ((New.IStructuredMethodFactory)factory).Create(null!));
    }

    private static New.StructuredMethodConstruction Construction()
    {
        var draft = DraftMethod(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));
        return ((New.IStructuredMethodDraftAdapter)new New.StructuredMethodDraftAdapter(
            new New.StructuredBlockDefinitionFactory(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupCollector(),
            new New.ExceptionGroupIdAssigner(),
            new global::NetWasm.Compiler.ControlFlow.Draft.ExceptionGroupParentMapBuilder(
                new global::NetWasm.Compiler.ControlFlow.Structuring.ExceptionScopeFinder()),
            new New.StructuredControlFlowProjector(),
            new New.StructuredExceptionGroupProjector(
                new New.StructuredControlFlowProjector()))).Adapt(draft);
    }

    private sealed class RecordingValidator : New.IStructuredMethodValidator
    {
        public int CallCount { get; private set; }

        public New.StructuredMethod? Method { get; private set; }

        public Exception? Exception { get; init; }

        public void Validate(New.StructuredMethod method)
        {
            CallCount++;
            Method = method;
            if (Exception is not null) throw Exception;
        }
    }
}
