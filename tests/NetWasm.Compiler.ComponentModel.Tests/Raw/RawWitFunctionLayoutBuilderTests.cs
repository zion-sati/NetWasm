using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Functions;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawWitFunctionLayoutBuilderTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PreservesTheWitDeclarationAndDelegatesTheResolvedFunction(WasmTarget target)
    {
        var dependencies = new RecordingDependencies();
        var document = new WitDocument([], [], [], [], "{}");
        var declaration = new WitFunction("[method]file.read", [], null, new("method", 3));

        var product = Create(dependencies).Build(document, "example:files@1.0.0/api", declaration, target);

        Assert.Same(declaration, product.Declaration);
        Assert.Same(dependencies.Layout, product.Layout);
        Assert.Equal(["function", "layout"], dependencies.Calls);
        Assert.Equal((document, "example:files@1.0.0/api", declaration), dependencies.FunctionRequest);
        Assert.Equal((dependencies.Function, target), dependencies.LayoutRequest);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, "cm32p2|example:files/api@1")]
    [InlineData(WasmTarget.Wasm64, "cm64p2|example:files/api@1")]
    public void RealCompositionRetainsAliasResourceIdentityAndWitFlagsNames(WasmTarget target, string module)
    {
        var resource = Type(0, "file", "\"resource\"");
        var alias = Type(1, "file-alias", """{"type":0}""");
        var borrow = Type(2, null, """{"handle":{"borrow":1}}""");
        var flags = Type(3, "permissions", """{"flags":{"flags":[{"name":"can-read"},{"name":"can-write"}]}}""");
        var document = new WitDocument([], [], [], [resource, alias, borrow, flags], "{}");
        var declaration = new WitFunction("[method]file.permissions", [new("self", new WitTypeReference.Defined(2))],
            new WitTypeReference.Defined(3), new("method", 0));
        var builder = Assert.IsAssignableFrom<IRawWitFunctionLayoutBuilder>(new RawWitFunctionLayoutBuilder(
            new WitCanonicalFunctionBuilder(new WitCanonicalTypeResolver()),
            new RawCanonicalFunctionLayoutPlanner(new RawCanonicalImportIdentityFormatter(),
                new CanonicalAbiSignaturePlanner(new CanonicalAbiTypeFlattener()), new CanonicalAbiMemoryLayoutPlanner())));

        var product = builder.Build(document, "example:files@1.0.0/api", declaration, target);

        Assert.Same(declaration, product.Declaration);
        Assert.Equal(new WitTypeReference.Defined(3), product.Declaration.Result);
        Assert.Equal("method", product.Declaration.Kind.Name);
        Assert.Equal(0, product.Declaration.Kind.ResourceType);
        Assert.Equal(module, product.Layout.Module);
        Assert.Equal(declaration.Name, product.Layout.Name);
        Assert.Equal(target, product.Layout.Target);
        var parameter = Assert.Single(product.Layout.Function.Parameters);
        Assert.Equal(CanonicalAbiTypeKind.BorrowedResource, parameter.Type.Kind);
        Assert.Equal(0, parameter.Type.ResourceTypeId);
        Assert.Equal(CanonicalAbiTypeKind.Flags, product.Layout.Function.Result!.Kind);
        Assert.Equal(2, product.Layout.Function.Result.FlagsCount);
        Assert.Equal([CliValueKind.I4], product.Layout.Signature.Parameters);
        Assert.Equal(CliValueKind.I4, product.Layout.Signature.Result);
        Assert.Equal(4, product.Layout.ParameterMemory.Size);
        Assert.Equal(1, product.Layout.ResultMemory!.Size);
        Assert.Equal(["can-read", "can-write"], flags.Kind.GetProperty("flags").GetProperty("flags")
            .EnumerateArray().Select(flag => flag.GetProperty("name").GetString()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void DependencyFailureStopsComposition(int failingCall)
    {
        var dependencies = new RecordingDependencies { FailingCall = failingCall };

        Assert.Same(dependencies.Failure, Assert.Throws<InvalidOperationException>(() => Create(dependencies).Build(
            new([], [], [], [], "{}"), "", new("run", [], null, new("freestanding")), WasmTarget.Wasm64)));
        Assert.Equal(failingCall, dependencies.Calls.Count);
    }

    [Fact]
    public void InvalidInputsAndMissingCapabilitiesFailBeforeDelegation()
    {
        var dependencies = new RecordingDependencies();
        var builder = Create(dependencies);
        var document = new WitDocument([], [], [], [], "{}");
        var declaration = new WitFunction("run", [], null, new("freestanding"));

        Assert.Throws<ArgumentNullException>(() => builder.Build(null!, "", declaration, WasmTarget.Wasm32));
        Assert.Throws<ArgumentNullException>(() => builder.Build(document, null!, declaration, WasmTarget.Wasm32));
        Assert.Throws<ArgumentNullException>(() => builder.Build(document, "", null!, WasmTarget.Wasm32));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(document, "", declaration, (WasmTarget)99));
        Assert.Throws<ArgumentNullException>(() => new RawWitFunctionLayoutBuilder(null!, dependencies));
        Assert.Throws<ArgumentNullException>(() => new RawWitFunctionLayoutBuilder(dependencies, null!));
        Assert.Empty(dependencies.Calls);
    }

    private static IRawWitFunctionLayoutBuilder Create(RecordingDependencies dependencies) =>
        Assert.IsAssignableFrom<IRawWitFunctionLayoutBuilder>(new RawWitFunctionLayoutBuilder(dependencies, dependencies));

    private static WitTypeDefinition Type(int id, string? name, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(id, name, document.RootElement.Clone(), null);
    }

    private sealed class RecordingDependencies : IWitCanonicalFunctionBuilder, IRawCanonicalFunctionLayoutPlanner
    {
        public List<string> Calls { get; } = [];
        public int FailingCall { get; init; }
        public InvalidOperationException Failure { get; } = new();
        public CanonicalAbiFunction Function { get; } = new("", "resolved", default, [], null);
        public RawCanonicalFunctionLayout Layout { get; } = new(WasmTarget.Wasm32,
            new("", "resolved", default, [], null), "module", "member",
            new([], CliValueKind.Void, [], [], false, false), new(0, 1, []), null);
        public (WitDocument, string, WitFunction)? FunctionRequest { get; private set; }
        public (CanonicalAbiFunction, WasmTarget)? LayoutRequest { get; private set; }

        public CanonicalAbiFunction Build(WitDocument document, string interfaceName, WitFunction witFunction)
        {
            Enter("function");
            FunctionRequest = (document, interfaceName, witFunction);
            return Function;
        }

        public RawCanonicalFunctionLayout Plan(CanonicalAbiFunction abiFunction, WasmTarget target)
        {
            Enter("layout");
            LayoutRequest = (abiFunction, target);
            return Layout;
        }

        private void Enter(string name)
        {
            Calls.Add(name);
            if (Calls.Count == FailingCall) throw Failure;
        }
    }
}
