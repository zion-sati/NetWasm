using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawResourceImportLayoutBuilderTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ProjectsTheResourceAndWrapsTheExactIntrinsicLayout(WasmTarget target)
    {
        var resources = new RecordingResources();
        var declaration = new RawWitImportDeclaration.Resource("example:test@1.0.0/api", new(0, "file", default, 0), CanonicalAbiFunctionKind.ExportedResourceNew);
        var document = new WitDocument([], [], [], [], "{}");

        var result = Assert.IsType<RawWitImportLayout.Resource>(Create(resources).Build(new(document, declaration, target)));

        Assert.Equal((document, declaration, target), resources.Request);
        Assert.Same(resources.Layout, result.Intrinsic);
        Assert.Equal(resources.Layout.Target, result.Target);
        Assert.Same(resources.Layout.Identity, result.Identity);
        Assert.Same(resources.Layout.Signature, result.Signature);
    }

    [Fact]
    public void RejectsMissingOrWrongDeclarationBeforeDelegation()
    {
        var resources = new RecordingResources();
        var builder = Create(resources);
        Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        Assert.Throws<CompilerException>(() => builder.Build(new(new([], [], [], [], "{}"), null!, WasmTarget.Wasm32)));
        Assert.Throws<CompilerException>(() => builder.Build(new(new([], [], [], [], "{}"),
            new RawWitImportDeclaration.Callable("", new("run", [], null, new("freestanding"))), WasmTarget.Wasm32)));
        Assert.Throws<ArgumentNullException>(() => new RawResourceImportLayoutBuilder(null!));
        Assert.Null(resources.Request);
    }

    [Fact]
    public void PreservesResourceLayoutFailure()
    {
        var resources = new RecordingResources { Failure = new() };
        Assert.Same(resources.Failure, Assert.Throws<InvalidOperationException>(() => Create(resources).Build(new(
            new([], [], [], [], "{}"), new RawWitImportDeclaration.Resource("", null!, CanonicalAbiFunctionKind.ImportedResourceDrop), WasmTarget.Wasm64))));
        Assert.NotNull(resources.Request);
    }

    private static IRawWitImportLayoutBuilder Create(IRawResourceIntrinsicLayoutPlanner resources) =>
        Assert.IsAssignableFrom<IRawWitImportLayoutBuilder>(new RawResourceImportLayoutBuilder(resources));

    private sealed class RecordingResources : IRawResourceIntrinsicLayoutPlanner
    {
        public (WitDocument, RawWitImportDeclaration.Resource, WasmTarget)? Request { get; private set; }
        public InvalidOperationException? Failure { get; init; }
        public RawResourceIntrinsicLayout Layout { get; } = new(WasmTarget.Wasm32,
            new("", new(0, "file", default, 0), CanonicalAbiFunctionKind.ImportedResourceDrop),
            new("physical", "member"), new([CliValueKind.I4], CliValueKind.Void, [CliValueKind.I4], [], false, false));

        public RawResourceIntrinsicLayout Plan(WitDocument document, RawWitImportDeclaration.Resource declaration, WasmTarget target)
        {
            Request = (document, declaration, target);
            if (Failure is not null) throw Failure;
            return Layout;
        }
    }
}
