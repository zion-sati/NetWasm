using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawCallableImportLayoutBuilderTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ProjectsTheCallableAndWrapsTheExactFunctionLayout(WasmTarget target)
    {
        var functions = new RecordingFunctions();
        var declaration = new RawWitImportDeclaration.Callable("example:test@1.0.0/api", new("call", [], null, new("freestanding")));
        var document = new WitDocument([], [], [], [], "{}");

        var result = Assert.IsType<RawWitImportLayout.Callable>(Create(functions).Build(new(document, declaration, target)));

        Assert.Equal((document, declaration.InterfaceName, declaration.Definition, target), functions.Request);
        Assert.Same(functions.Layout, result.Function);
        Assert.Equal(functions.Layout.Layout.Target, result.Target);
        Assert.Equal(new RawCanonicalImportIdentity("physical", "member"), result.Identity);
        Assert.Same(functions.Layout.Layout.Signature, result.Signature);
    }

    [Fact]
    public void RejectsMissingOrWrongDeclarationBeforeDelegation()
    {
        var functions = new RecordingFunctions();
        var builder = Create(functions);
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        Assert.Throws<CompilerException>(() => builder.Build(new(new([], [], [], [], "{}"), null!, WasmTarget.Wasm32)));
        Assert.Throws<CompilerException>(() => builder.Build(new(new([], [], [], [], "{}"),
            new RawWitImportDeclaration.Resource("", null!, CanonicalAbiFunctionKind.ImportedResourceDrop), WasmTarget.Wasm32)));
        Assert.Throws<ArgumentNullException>(() => new RawCallableImportLayoutBuilder(null!));
        Assert.Null(functions.Request);
    }

    [Fact]
    public void PreservesFunctionLayoutFailure()
    {
        var functions = new RecordingFunctions { Failure = new() };
        Assert.Same(functions.Failure, Assert.Throws<InvalidOperationException>(() => Create(functions).Build(new(
            new([], [], [], [], "{}"), new RawWitImportDeclaration.Callable("", new("run", [], null, new("freestanding"))), WasmTarget.Wasm64))));
        Assert.NotNull(functions.Request);
    }

    private static IRawWitImportLayoutBuilder Create(IRawWitFunctionLayoutBuilder functions) =>
        Assert.IsAssignableFrom<IRawWitImportLayoutBuilder>(new RawCallableImportLayoutBuilder(functions));

    private sealed class RecordingFunctions : IRawWitFunctionLayoutBuilder
    {
        public (WitDocument, string, WitFunction, WasmTarget)? Request { get; private set; }
        public InvalidOperationException? Failure { get; init; }
        public RawWitFunctionLayout Layout { get; } = new(new("returned", [], null, new("freestanding")),
            new(WasmTarget.Wasm32, new("", "returned", default, [], null), "physical", "member",
                new([], CliValueKind.Void, [], [], false, false), new(0, 1, []), null));

        public RawWitFunctionLayout Build(WitDocument document, string interfaceName, WitFunction witFunction, WasmTarget target)
        {
            Request = (document, interfaceName, witFunction, target);
            if (Failure is not null) throw Failure;
            return Layout;
        }
    }
}
