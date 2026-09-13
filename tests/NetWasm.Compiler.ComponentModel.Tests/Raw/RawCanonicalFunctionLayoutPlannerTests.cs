using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawCanonicalFunctionLayoutPlannerTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DelegatesCanonicalPlanningAndPreservesTheDeclaredTypes(WasmTarget target)
    {
        var dependencies = new RecordingPlanners();
        var first = Type(CanonicalAbiTypeKind.U8);
        var second = Type(CanonicalAbiTypeKind.Text);
        var result = Type(CanonicalAbiTypeKind.U64);
        var function = Function([new("first", first), new("second", second)], result) with
        {
            FunctionName = "[method]descriptor.read",
        };

        var layout = CreatePlanner(dependencies).Plan(function, target);

        Assert.Equal(target, layout.Target);
        Assert.Same(function, layout.Function);
        Assert.Equal(dependencies.Identity.Module, layout.Module);
        Assert.Equal(dependencies.Identity.Name, layout.Name);
        Assert.Same(dependencies.Signature, layout.Signature);
        Assert.Same(dependencies.ParameterMemory, layout.ParameterMemory);
        Assert.Same(dependencies.ResultMemory, layout.ResultMemory);
        Assert.Equal(["identity", "signature", "parameters", "result"], dependencies.Calls);
        Assert.Equal((function, target), dependencies.IdentityRequest);
        Assert.Equal((function, CanonicalAbiDirection.LoweredImport), dependencies.SignatureRequest);
        Assert.Collection(dependencies.MemoryRequests,
            request =>
            {
                Assert.Equal(target, request.Target);
                Assert.Equal(CanonicalAbiTypeKind.Tuple, request.Type.Kind);
                Assert.Equal([new CanonicalAbiField("first", first), new("second", second)], request.Type.Fields);
            },
            request =>
            {
                Assert.Equal(target, request.Target);
                Assert.Same(result, request.Type);
            });
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EmptyRootFunctionHasNoResultAllocationOrInventedParameters(WasmTarget target)
    {
        var dependencies = new RecordingPlanners();
        var function = Function([], null) with { InterfaceName = "" };

        var layout = CreatePlanner(dependencies).Plan(function, target);

        Assert.Equal(dependencies.Identity.Module, layout.Module);
        Assert.Null(layout.ResultMemory);
        Assert.Equal(["identity", "signature", "parameters"], dependencies.Calls);
        Assert.Empty(Assert.Single(dependencies.MemoryRequests).Type.Fields);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 4, 16, 24)]
    [InlineData(WasmTarget.Wasm64, 8, 24, 32)]
    public void RealPlannersRetainMixedWidthOffsetsAndIndirectResultSemantics(
        WasmTarget target, int textOffset, int numberOffset, int parameterSize)
    {
        var result = Type(CanonicalAbiTypeKind.Option) with { ElementType = Type(CanonicalAbiTypeKind.U64) };
        var function = Function([
            new("flag", Type(CanonicalAbiTypeKind.U8)),
            new("label", Type(CanonicalAbiTypeKind.Text)),
            new("number", Type(CanonicalAbiTypeKind.U64)),
        ], result);

        var layout = CreateRealPlanner().Plan(function, target);

        Assert.Equal([CliValueKind.I4, CliValueKind.ManagedAddress, CliValueKind.ManagedAddress,
            CliValueKind.I8, CliValueKind.ManagedAddress], layout.Signature.Parameters);
        Assert.False(layout.Signature.IndirectParameters);
        Assert.True(layout.Signature.IndirectResult);
        Assert.Equal(CliValueKind.Void, layout.Signature.Result);
        Assert.Equal([CliValueKind.I4, CliValueKind.I8], layout.Signature.FlatResults);
        Assert.Equal(parameterSize, layout.ParameterMemory.Size);
        Assert.Equal(8, layout.ParameterMemory.Alignment);
        Assert.Equal([0, textOffset, numberOffset], layout.ParameterMemory.Fields.Select(field => field.Offset));
        Assert.NotNull(layout.ResultMemory);
        Assert.Equal(16, layout.ResultMemory.Size);
        Assert.Equal(8, layout.ResultMemory.Alignment);
        Assert.Equal(8, layout.ResultMemory.PayloadOffset);
        Assert.Equal(1, layout.ResultMemory.DiscriminantSize);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LargeParameterTupleUsesExistingIndirectParameterConvention(WasmTarget target)
    {
        var parameters = Enumerable.Range(0, 17)
            .Select(index => new CanonicalAbiParameter("p" + index, Type(CanonicalAbiTypeKind.U32)))
            .ToImmutableArray();

        var layout = CreateRealPlanner().Plan(Function(parameters, null), target);

        Assert.True(layout.Signature.IndirectParameters);
        Assert.False(layout.Signature.IndirectResult);
        Assert.Equal([CliValueKind.ManagedAddress], layout.Signature.Parameters);
        Assert.Equal(CliValueKind.Void, layout.Signature.Result);
        Assert.Equal(17, layout.Signature.FlatParameters.Length);
        Assert.All(layout.Signature.FlatParameters, value => Assert.Equal(CliValueKind.I4, value));
        Assert.Equal(68, layout.ParameterMemory.Size);
        Assert.Equal(4, layout.ParameterMemory.Alignment);
        Assert.Equal(Enumerable.Range(0, 17).Select(index => index * 4),
            layout.ParameterMemory.Fields.Select(field => field.Offset));
        Assert.Null(layout.ResultMemory);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DirectScalarAndUnitResultsKeepTheirDistinctShapes(WasmTarget target)
    {
        var planner = CreateRealPlanner();
        var scalar = planner.Plan(Function([], Type(CanonicalAbiTypeKind.S32)), target);
        var unit = planner.Plan(Function([], Type(CanonicalAbiTypeKind.Unit)), target);
        var absent = planner.Plan(Function([], null), target);

        Assert.Equal(CliValueKind.I4, scalar.Signature.Result);
        Assert.False(scalar.Signature.IndirectResult);
        Assert.Equal(4, scalar.ResultMemory!.Size);
        Assert.Equal(4, scalar.ResultMemory.Alignment);
        Assert.Equal(CliValueKind.Void, unit.Signature.Result);
        Assert.Equal(0, unit.ResultMemory!.Size);
        Assert.Equal(1, unit.ResultMemory.Alignment);
        Assert.Equal(CliValueKind.Void, absent.Signature.Result);
        Assert.Null(absent.ResultMemory);
        Assert.Equal(0, absent.ParameterMemory.Size);
        Assert.Equal(1, absent.ParameterMemory.Alignment);
    }

    [Theory]
    [InlineData(CanonicalAbiFunctionKind.ImportedResourceDrop)]
    [InlineData(CanonicalAbiFunctionKind.ExportedResourceNew)]
    [InlineData(CanonicalAbiFunctionKind.ExportedResourceRep)]
    [InlineData(CanonicalAbiFunctionKind.ExportedResourceDrop)]
    [InlineData(CanonicalAbiFunctionKind.ExportedResourceDestructor)]
    public void ResourceBuiltInsDoNotUseAnOrdinaryFunctionPlan(CanonicalAbiFunctionKind kind)
    {
        var dependencies = new RecordingPlanners();

        Assert.Throws<CompilerException>(() => CreatePlanner(dependencies).Plan(
            Function([], null) with { Kind = kind }, WasmTarget.Wasm32));
        Assert.Empty(dependencies.Calls);
    }

    [Fact]
    public void InvalidDeclarationsFailBeforeDelegation()
    {
        var dependencies = new RecordingPlanners();
        var planner = CreatePlanner(dependencies);
        var function = Function([], null);
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!, WasmTarget.Wasm32));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(function with { InterfaceName = null! }, WasmTarget.Wasm32));
        Assert.Throws<ArgumentException>(() => planner.Plan(function with { FunctionName = " " }, WasmTarget.Wasm32));
        Assert.Throws<ArgumentOutOfRangeException>(() => planner.Plan(function, (WasmTarget)99));
        Assert.Throws<CompilerException>(() => planner.Plan(function with { Parameters = default }, WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => planner.Plan(function with { Parameters = [null!] }, WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => planner.Plan(function with { Parameters = [new("value", null!)] }, WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => planner.Plan(function with { Parameters = [new(" ", Type(CanonicalAbiTypeKind.U8))] }, WasmTarget.Wasm32));
        Assert.Throws<CompilerException>(() => planner.Plan(function with
        {
            Parameters = [new("value", Type(CanonicalAbiTypeKind.U8)), new("value", Type(CanonicalAbiTypeKind.U16))],
        }, WasmTarget.Wasm32));
        Assert.Empty(dependencies.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void DependencyFailureDoesNotProduceAPartialPlan(int failingCall)
    {
        var dependencies = new RecordingPlanners { FailingCall = failingCall };

        Assert.Same(dependencies.Failure, Assert.Throws<InvalidOperationException>(() => CreatePlanner(dependencies).Plan(
            Function([], Type(CanonicalAbiTypeKind.U8)), WasmTarget.Wasm64)));
        Assert.Equal(failingCall, dependencies.Calls.Count);
    }

    [Fact]
    public void RequiresAllPlanningCapabilities()
    {
        var dependencies = new RecordingPlanners();

        Assert.Throws<ArgumentNullException>(() => new RawCanonicalFunctionLayoutPlanner(null!, dependencies, dependencies));
        Assert.Throws<ArgumentNullException>(() => new RawCanonicalFunctionLayoutPlanner(dependencies, null!, dependencies));
        Assert.Throws<ArgumentNullException>(() => new RawCanonicalFunctionLayoutPlanner(dependencies, dependencies, null!));
        Assert.Empty(dependencies.Calls);
    }

    private static IRawCanonicalFunctionLayoutPlanner CreatePlanner(RecordingPlanners dependencies) =>
        Assert.IsAssignableFrom<IRawCanonicalFunctionLayoutPlanner>(new RawCanonicalFunctionLayoutPlanner(dependencies, dependencies, dependencies));

    private static IRawCanonicalFunctionLayoutPlanner CreateRealPlanner() =>
        Assert.IsAssignableFrom<IRawCanonicalFunctionLayoutPlanner>(new RawCanonicalFunctionLayoutPlanner(
            new RawCanonicalImportIdentityFormatter(),
            new CanonicalAbiSignaturePlanner(new CanonicalAbiTypeFlattener()), new CanonicalAbiMemoryLayoutPlanner()));

    private static CanonicalAbiType Type(CanonicalAbiTypeKind kind) =>
        new(kind, CliTypeIdentity.FromStackKind(CliValueKind.Unknown));

    private static CanonicalAbiFunction Function(ImmutableArray<CanonicalAbiParameter> parameters, CanonicalAbiType? result) =>
        new("sample:raw@1.2.3/io", "read", default, parameters, result);

    private sealed class RecordingPlanners : IRawCanonicalImportIdentityFormatter,
        ICanonicalAbiSignaturePlanner, ICanonicalAbiMemoryLayoutPlanner
    {
        public List<string> Calls { get; } = [];
        public int FailingCall { get; init; }
        public InvalidOperationException Failure { get; } = new();
        public RawCanonicalImportIdentity Identity { get; } = new("physical-module", "physical-member");
        public (CanonicalAbiFunction, WasmTarget)? IdentityRequest { get; private set; }
        public (CanonicalAbiFunction, CanonicalAbiDirection)? SignatureRequest { get; private set; }
        public List<(CanonicalAbiType Type, WasmTarget Target)> MemoryRequests { get; } = [];
        public CanonicalAbiCoreSignature Signature { get; } = new([], CliValueKind.Void, [], [], false, false);
        public CanonicalAbiMemoryLayout ParameterMemory { get; } = new(24, 8, []);
        public CanonicalAbiMemoryLayout ResultMemory { get; } = new(8, 8, []);

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

        public CanonicalAbiMemoryLayout Plan(CanonicalAbiType type, WasmTarget target)
        {
            Enter(MemoryRequests.Count == 0 ? "parameters" : "result");
            MemoryRequests.Add((type, target));
            return MemoryRequests.Count == 1 ? ParameterMemory : ResultMemory;
        }

        private void Enter(string name)
        {
            Calls.Add(name);
            if (Calls.Count == FailingCall) throw Failure;
        }
    }
}
