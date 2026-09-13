using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawModuleLinkInputValidatorTests
{
    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void DelegatesExistingInputChecksWithoutComponentEntryPoint(string width)
    {
        var inputs = new RecordingInputs();
        var request = new RawModuleLinkRequest("application", "runtime", "output", new(width, "0.2", "utf8"));
        var validator = CreateValidator(inputs);

        validator.Validate(request);

        Assert.Equal(new ComponentCoreModuleLinkRequest("application", "runtime", "output", request.Target), inputs.Request);
    }

    [Theory]
    [InlineData("wasm16", "0.2", "utf8")]
    [InlineData("wasm32", "0.1", "utf8")]
    [InlineData("wasm64", "0.3", "utf8")]
    [InlineData("wasm32", "0.2", "utf16")]
    public void RejectsUnsupportedTargetBeforeInputAccess(string width, string wasi, string encoding)
    {
        var inputs = new RecordingInputs();
        var validator = CreateValidator(inputs);

        Assert.Throws<CompilerException>(() => validator.Validate(new("application", "runtime", "output", new(width, wasi, encoding))));

        Assert.Null(inputs.Request);
    }

    [Theory]
    [InlineData("same", "./same", "output")]
    [InlineData("same", "runtime", "./same")]
    [InlineData("application", "same", "./same")]
    [InlineData("SAME", "runtime", "same")]
    public void RejectsPortablePathCollisionsBeforeInputAccess(string application, string runtime, string output)
    {
        var inputs = new RecordingInputs();
        var validator = CreateValidator(inputs);

        Assert.Throws<CompilerException>(() => validator.Validate(new(application, runtime, output, ComponentTarget.Wasm32Wasi02)));

        Assert.Null(inputs.Request);
    }

    [Fact]
    public void RejectsMissingInputsBeforeDelegation()
    {
        var inputs = new RecordingInputs();
        var validator = CreateValidator(inputs);
        var request = new RawModuleLinkRequest("application", "runtime", "output", ComponentTarget.Wasm32Wasi02);

        Assert.Throws<ArgumentNullException>(() => new RawModuleLinkInputValidator(null!));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(request with { Target = null! }));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(request with { ApplicationModulePath = null! }));
        Assert.Throws<ArgumentException>(() => validator.Validate(request with { RuntimeModulePath = " " }));
        Assert.Throws<ArgumentException>(() => validator.Validate(request with { OutputPath = "" }));
        Assert.Null(inputs.Request);
    }

    [Fact]
    public void PreservesInputFailure()
    {
        var failure = new InvalidOperationException();
        var inputs = new RecordingInputs { Failure = failure };
        var validator = CreateValidator(inputs);

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => validator.Validate(
            new("application", "runtime", "output", ComponentTarget.Wasm64Wasi02))));
        Assert.NotNull(inputs.Request);
    }

    private static IRawModuleLinkInputValidator CreateValidator(RecordingInputs inputs) =>
        Assert.IsAssignableFrom<IRawModuleLinkInputValidator>(new RawModuleLinkInputValidator(inputs));

    private sealed class RecordingInputs : IComponentCoreModuleInputValidator
    {
        public ComponentCoreModuleLinkRequest? Request { get; private set; }
        public Exception? Failure { get; init; }

        public void Validate(ComponentCoreModuleLinkRequest request)
        {
            Request = request;
            if (Failure is not null) throw Failure;
        }
    }
}
