using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawResourceIntrinsicLayoutPlannerTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, CanonicalAbiFunctionKind.ImportedResourceDrop, false)]
    [InlineData(WasmTarget.Wasm64, CanonicalAbiFunctionKind.ImportedResourceDrop, false)]
    [InlineData(WasmTarget.Wasm32, CanonicalAbiFunctionKind.ExportedResourceNew, true)]
    [InlineData(WasmTarget.Wasm64, CanonicalAbiFunctionKind.ExportedResourceNew, true)]
    [InlineData(WasmTarget.Wasm32, CanonicalAbiFunctionKind.ExportedResourceRep, true)]
    [InlineData(WasmTarget.Wasm64, CanonicalAbiFunctionKind.ExportedResourceRep, true)]
    [InlineData(WasmTarget.Wasm32, CanonicalAbiFunctionKind.ExportedResourceDrop, false)]
    [InlineData(WasmTarget.Wasm64, CanonicalAbiFunctionKind.ExportedResourceDrop, false)]
    public void DelegatesTheDeclaredHandleShapeWithoutMemoryPlanning(WasmTarget target, CanonicalAbiFunctionKind kind, bool returnsHandle)
    {
        var dependencies = new RecordingDependencies();
        var declaration = Declaration(kind);
        var document = new WitDocument([], [], [], [declaration.Definition], "{}");

        var layout = Create(dependencies).Plan(document, declaration, target);

        Assert.Equal(target, layout.Target);
        Assert.Same(declaration, layout.Declaration);
        Assert.Same(dependencies.Identity, layout.Identity);
        Assert.Same(dependencies.Signature, layout.Signature);
        Assert.Equal(["type", "identity", "signature"], dependencies.Calls);
        Assert.Equal((document, new WitTypeReference.Primitive("u32")), dependencies.TypeRequest);
        var function = dependencies.IdentityRequest!.Value.Function;
        Assert.Equal(target, dependencies.IdentityRequest.Value.Target);
        Assert.Equal(declaration.InterfaceName, function.InterfaceName);
        Assert.Equal(kind, function.Kind);
        Assert.Equal("file", function.ResourceName);
        var handle = Assert.Single(function.Parameters);
        Assert.Equal("handle", handle.Name);
        Assert.Same(dependencies.Handle, handle.Type);
        Assert.Same(returnsHandle ? dependencies.Handle : null, function.Result);
        Assert.Equal((function, CanonicalAbiDirection.LoweredImport), dependencies.SignatureRequest);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, "cm32p2", CanonicalAbiFunctionKind.ImportedResourceDrop, "", "drop", CliValueKind.Void)]
    [InlineData(WasmTarget.Wasm64, "cm64p2", CanonicalAbiFunctionKind.ImportedResourceDrop, "", "drop", CliValueKind.Void)]
    [InlineData(WasmTarget.Wasm32, "cm32p2", CanonicalAbiFunctionKind.ExportedResourceNew, "_ex_", "new", CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm64, "cm64p2", CanonicalAbiFunctionKind.ExportedResourceNew, "_ex_", "new", CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm32, "cm32p2", CanonicalAbiFunctionKind.ExportedResourceRep, "_ex_", "rep", CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm64, "cm64p2", CanonicalAbiFunctionKind.ExportedResourceRep, "_ex_", "rep", CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm32, "cm32p2", CanonicalAbiFunctionKind.ExportedResourceDrop, "_ex_", "drop", CliValueKind.Void)]
    [InlineData(WasmTarget.Wasm64, "cm64p2", CanonicalAbiFunctionKind.ExportedResourceDrop, "_ex_", "drop", CliValueKind.Void)]
    public void ExistingCanonicalPlannersProduceTargetIndependentHandleSignatures(
        WasmTarget target, string prefix, CanonicalAbiFunctionKind kind, string exported, string suffix, CliValueKind result)
    {
        var declaration = Declaration(kind);
        var planner = Assert.IsAssignableFrom<IRawResourceIntrinsicLayoutPlanner>(new RawResourceIntrinsicLayoutPlanner(
            new WitCanonicalTypeResolver(), new RawCanonicalImportIdentityFormatter(),
            new CanonicalAbiSignaturePlanner(new CanonicalAbiTypeFlattener())));

        var layout = planner.Plan(new([], [], [], [declaration.Definition], "{}"), declaration, target);

        Assert.Equal(new RawCanonicalImportIdentity(prefix + "|" + exported + "example:files/api@1", "file_" + suffix), layout.Identity);
        Assert.Equal([CliValueKind.I4], layout.Signature.Parameters);
        Assert.Equal([CliValueKind.I4], layout.Signature.FlatParameters);
        Assert.Equal(result, layout.Signature.Result);
        Assert.Equal(result == CliValueKind.Void ? [] : new[] { CliValueKind.I4 }, layout.Signature.FlatResults);
        Assert.False(layout.Signature.IndirectParameters);
        Assert.False(layout.Signature.IndirectResult);
        Assert.Equal(0, layout.Declaration.Definition.Id);
    }

    [Theory]
    [InlineData(CanonicalAbiFunctionKind.Function)]
    [InlineData(CanonicalAbiFunctionKind.ExportedResourceDestructor)]
    [InlineData((CanonicalAbiFunctionKind)99)]
    public void NonImportResourceKindsFailBeforeDelegation(CanonicalAbiFunctionKind kind)
    {
        var dependencies = new RecordingDependencies();

        Assert.Throws<CompilerException>(() => Create(dependencies).Plan(new([], [], [], [], "{}"), Declaration(kind), WasmTarget.Wasm32));
        Assert.Empty(dependencies.Calls);
    }

    [Theory]
    [InlineData("\"other\"")]
    [InlineData("""{"type":0}""")]
    public void AliasesAndNonResourcesCannotBecomeIntrinsics(string json)
    {
        var dependencies = new RecordingDependencies();
        using var parsed = JsonDocument.Parse(json);
        var declaration = Declaration(CanonicalAbiFunctionKind.ImportedResourceDrop);
        declaration = declaration with { Definition = declaration.Definition with { Kind = parsed.RootElement.Clone() } };

        Assert.Throws<CompilerException>(() => Create(dependencies).Plan(new([], [], [], [], "{}"), declaration, WasmTarget.Wasm32));
        Assert.Empty(dependencies.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DependencyFailureStopsLaterPlanning(int failingCall)
    {
        var dependencies = new RecordingDependencies { FailingCall = failingCall };

        Assert.Same(dependencies.Failure, Assert.Throws<InvalidOperationException>(() => Create(dependencies).Plan(
            new([], [], [], [], "{}"), Declaration(CanonicalAbiFunctionKind.ExportedResourceRep), WasmTarget.Wasm64)));
        Assert.Equal(failingCall, dependencies.Calls.Count);
    }

    [Fact]
    public void MissingInputsAndInvalidTargetsFailBeforeDelegation()
    {
        var dependencies = new RecordingDependencies();
        var planner = Create(dependencies);
        var document = new WitDocument([], [], [], [], "{}");
        var declaration = Declaration(CanonicalAbiFunctionKind.ImportedResourceDrop);

        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!, declaration, WasmTarget.Wasm32));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(document, null!, WasmTarget.Wasm32));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(document, declaration with { Definition = null! }, WasmTarget.Wasm32));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(document, declaration with { InterfaceName = null! }, WasmTarget.Wasm32));
        Assert.Throws<ArgumentException>(() => planner.Plan(document, declaration with { Definition = declaration.Definition with { Name = " " } }, WasmTarget.Wasm32));
        Assert.Throws<ArgumentOutOfRangeException>(() => planner.Plan(document, declaration, (WasmTarget)99));
        Assert.Throws<ArgumentNullException>(() => new RawResourceIntrinsicLayoutPlanner(null!, dependencies, dependencies));
        Assert.Throws<ArgumentNullException>(() => new RawResourceIntrinsicLayoutPlanner(dependencies, null!, dependencies));
        Assert.Throws<ArgumentNullException>(() => new RawResourceIntrinsicLayoutPlanner(dependencies, dependencies, null!));
        Assert.Empty(dependencies.Calls);
    }

    private static IRawResourceIntrinsicLayoutPlanner Create(RecordingDependencies dependencies) =>
        Assert.IsAssignableFrom<IRawResourceIntrinsicLayoutPlanner>(new RawResourceIntrinsicLayoutPlanner(dependencies, dependencies, dependencies));

    private static RawWitImportDeclaration.Resource Declaration(CanonicalAbiFunctionKind kind)
    {
        using var parsed = JsonDocument.Parse("\"resource\"");
        return new("example:files@1.0.0/api", new(0, "file", parsed.RootElement.Clone(), 0), kind);
    }

    private sealed class RecordingDependencies : IWitCanonicalTypeResolver, IRawCanonicalImportIdentityFormatter, ICanonicalAbiSignaturePlanner
    {
        public List<string> Calls { get; } = [];
        public int FailingCall { get; init; }
        public InvalidOperationException Failure { get; } = new();
        public CanonicalAbiType Handle { get; } = new(CanonicalAbiTypeKind.U32, CliTypeIdentity.FromStackKind(CliValueKind.Unknown));
        public RawCanonicalImportIdentity Identity { get; } = new("physical-module", "physical-member");
        public CanonicalAbiCoreSignature Signature { get; } = new([CliValueKind.I4], CliValueKind.Void, [CliValueKind.I4], [], false, false);
        public (WitDocument, WitTypeReference)? TypeRequest { get; private set; }
        public (CanonicalAbiFunction Function, WasmTarget Target)? IdentityRequest { get; private set; }
        public (CanonicalAbiFunction, CanonicalAbiDirection)? SignatureRequest { get; private set; }

        public CanonicalAbiType Resolve(WitDocument document, WitTypeReference reference)
        {
            Enter("type");
            TypeRequest = (document, reference);
            return Handle;
        }

        public RawCanonicalImportIdentity Format(CanonicalAbiFunction abiFunction, WasmTarget target)
        {
            Enter("identity");
            IdentityRequest = (abiFunction, target);
            return Identity;
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
