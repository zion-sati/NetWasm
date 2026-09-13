using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel.Functions;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitBindingFunctionModelBuilderTests
{
    [Theory]
    [InlineData(CanonicalAbiDirection.LoweredImport)]
    [InlineData(CanonicalAbiDirection.LiftedExport)]
    public void DelegatesFunctionConstructionAndPreservesSignatureDirection(CanonicalAbiDirection direction)
    {
        var dependencies = new RecordingDependencies();
        var builder = Assert.IsAssignableFrom<IWitBindingFunctionModelBuilder>(
            new WitBindingFunctionModelBuilder(dependencies, dependencies));
        var document = new WitDocument([], [], [], [], "{}");
        var declaration = new WitFunction("run", [], null, new("freestanding"));

        var model = builder.Build(document, "example:test@1.0.0/api", declaration, direction);

        Assert.Same(dependencies.Function, model.Function);
        Assert.Same(dependencies.Signature, model.Signature);
        Assert.Equal(["function", "signature"], dependencies.Calls);
        Assert.Equal((document, "example:test@1.0.0/api", declaration), dependencies.FunctionRequest);
        Assert.Equal((dependencies.Function, direction), dependencies.SignatureRequest);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void DependencyFailurePreventsLaterPlanning(int failingCall)
    {
        var dependencies = new RecordingDependencies { FailingCall = failingCall };
        var builder = Assert.IsAssignableFrom<IWitBindingFunctionModelBuilder>(
            new WitBindingFunctionModelBuilder(dependencies, dependencies));

        Assert.Same(dependencies.Failure, Assert.Throws<InvalidOperationException>(() => builder.Build(
            new([], [], [], [], "{}"), "", new("run", [], null, new("freestanding")), CanonicalAbiDirection.LoweredImport)));
        Assert.Equal(failingCall, dependencies.Calls.Count);
    }

    [Fact]
    public void BuildsCanonicalFunctionModelThroughContract()
    {
        var function = new WitFunction(
            "run",
            [new WitParameter("value", new WitTypeReference.Primitive("u32"))],
            new WitTypeReference.Primitive("u32"),
            new WitFunctionKind("freestanding"));
        var document = new WitDocument([], [], [], [], "{}");

        var model = Build(
            new WitBindingFunctionModelBuilder(
                new WitCanonicalFunctionBuilder(new WitCanonicalTypeResolver()),
                new CanonicalAbiSignaturePlanner(new CanonicalAbiTypeFlattener())),
            document,
            function);

        Assert.Equal("run", model.Function.FunctionName);
        Assert.Equal(CliValueKind.I4, model.Signature.Result);
        Assert.Equal([CliValueKind.I4], model.Signature.Parameters);
    }

    [Fact]
    public void RejectsMissingModelInputs()
    {
        var dependencies = new RecordingDependencies();
        var builder = Assert.IsAssignableFrom<IWitBindingFunctionModelBuilder>(
            new WitBindingFunctionModelBuilder(dependencies, dependencies));
        var document = new WitDocument([], [], [], [], "{}");
        var function = new WitFunction("run", [], null,
            new WitFunctionKind("freestanding"));

        Assert.Throws<ArgumentNullException>(() => builder.Build(
            null!, "api", function, CanonicalAbiDirection.LoweredImport));
        Assert.Throws<ArgumentNullException>(() => builder.Build(
            document, null!, function, CanonicalAbiDirection.LoweredImport));
        Assert.Throws<ArgumentNullException>(() => builder.Build(
            document, "api", null!, CanonicalAbiDirection.LoweredImport));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingFunctionModelBuilder(null!,
                new CanonicalAbiSignaturePlanner(new CanonicalAbiTypeFlattener())));
        Assert.Throws<ArgumentNullException>(() =>
            new WitBindingFunctionModelBuilder(new WitCanonicalFunctionBuilder(new WitCanonicalTypeResolver()), null!));
        Assert.Empty(dependencies.Calls);
    }

    private static WitBindingFunctionModel Build(
        object builder,
        WitDocument document,
        WitFunction function) =>
        ((IWitBindingFunctionModelBuilder)builder).Build(
            document,
            "example:test/api",
            function,
            CanonicalAbiDirection.LoweredImport);

    private sealed class RecordingDependencies : IWitCanonicalFunctionBuilder, ICanonicalAbiSignaturePlanner
    {
        public List<string> Calls { get; } = [];
        public int FailingCall { get; init; }
        public InvalidOperationException Failure { get; } = new();
        public CanonicalAbiFunction Function { get; } = new("", "resolved", default, [], null);
        public CanonicalAbiCoreSignature Signature { get; } = new([], CliValueKind.Void, [], [], false, false);
        public (WitDocument, string, WitFunction)? FunctionRequest { get; private set; }
        public (CanonicalAbiFunction, CanonicalAbiDirection)? SignatureRequest { get; private set; }

        public CanonicalAbiFunction Build(WitDocument document, string interfaceName, WitFunction witFunction)
        {
            Enter("function");
            FunctionRequest = (document, interfaceName, witFunction);
            return Function;
        }

        public CanonicalAbiCoreSignature Plan(CanonicalAbiFunction abiFunction, CanonicalAbiDirection direction)
        {
            Enter("signature");
            SignatureRequest = (abiFunction, direction);
            return Signature;
        }

        private void Enter(string name)
        {
            Calls.Add(name);
            if (Calls.Count == FailingCall) throw Failure;
        }
    }
}
