using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class NativeCallbackFunctionAppenderTests
{
    [Fact]
    public void AppendsPlannedThunksWithExactPhysicalTypesAndBoundaryPolicy()
    {
        var callback = CallbackPlan();
        var functions = new List<WasmFunctionDefinition>();
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var boundaries = new List<ManagedBoundaryPlanEntry>();
        var thunks = new RecordingThunkEmitter();
        var appender = Assert.IsAssignableFrom<INativeCallbackFunctionAppender>(
            new NativeCallbackFunctionAppender(thunks, new FixedBoundaryBuilder()));

        appender.Append(
            functions,
            4,
            indices,
            new([callback]),
            ModuleDataPlan.Empty with { NativeCallbackReadinessAddress = 256 },
            new(WasmModuleProfile.CoreApplication, true),
            new FixedFunctionIndexResolver(),
            boundaries);

        var function = Assert.Single(functions);
        Assert.Equal(callback.ThunkExportName, function.Name);
        Assert.Equal(callback.Abi.PhysicalSignature.ParameterTypes, function.Type.Parameters);
        Assert.Equal(callback.Abi.PhysicalSignature.ReturnType, function.Type.Result);
        Assert.Equal([7], function.Body);
        Assert.Equal(4, indices[callback.ThunkExportName]);
        Assert.Same(callback, thunks.Callback);
        var boundary = Assert.Single(boundaries);
        Assert.Equal(ManagedBoundaryKind.NativeCallback, boundary.Kind);
        Assert.Equal(ManagedBoundaryFailureDisposition.ReportAndTerminate,
            boundary.Disposition);
    }

    [Fact]
    public void EmptyPlanAddsNoFunctionsExportsOrBoundaries()
    {
        var functions = new List<WasmFunctionDefinition>();
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var boundaries = new List<ManagedBoundaryPlanEntry>();
        var thunks = new RecordingThunkEmitter();
        var appender = Assert.IsAssignableFrom<INativeCallbackFunctionAppender>(
            new NativeCallbackFunctionAppender(thunks, new FixedBoundaryBuilder()));

        appender.Append(
            functions,
            0,
            indices,
            NativeCallbackPlan.Empty,
            ModuleDataPlan.Empty,
            default,
            new FixedFunctionIndexResolver(),
            boundaries);

        Assert.Empty(functions);
        Assert.Empty(indices);
        Assert.Empty(boundaries);
        Assert.Null(thunks.Callback);
    }

    private static NativeCallbackMethodPlan CallbackPlan()
    {
        var program = new FakeProgram();
        var signature = MethodSignatureModel.Create(
            CliValueKind.I4,
            CliValueKind.I4,
            CliValueKind.NativeInt);
        var definition = program.GetMethod(EmitterTestSupport.EntryKey) with
        {
            Signature = signature,
            NativeCallback = new([], null, false, false),
        };
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "Test",
                "Callbacks",
                false),
            [],
            signature);
        var abi = NativeAbiTestSupport.ScalarSignaturePlanner().Plan(
            signature,
            NativeAbiSignatureKind.Callback,
            method.CanonicalName);
        return new(method, abi, "native", "native", "thunk", "getter", new(3));
    }

    private sealed class RecordingThunkEmitter : INativeCallbackThunkEmitter
    {
        public NativeCallbackMethodPlan? Callback { get; private set; }

        public byte[] Emit(
            NativeCallbackMethodPlan callback,
            ModuleDataPlan moduleData,
            RuntimeImportSelection runtimeImportSelection,
            IFunctionIndexResolver functionIndices)
        {
            Callback = callback;
            return [7];
        }
    }

    private sealed class FixedFunctionIndexResolver : IFunctionIndexResolver
    {
        public int Resolve(EntityKey method) => 30;
        public int Resolve(string method) => 30;
        public int Resolve(MethodInstanceModel method) => 30;
    }

    private sealed class FixedBoundaryBuilder : IManagedBoundaryPlanBuilder
    {
        public ManagedBoundaryPlanEntry Build(ManagedBoundaryPlanBuildRequest request) =>
            new(
                request.FunctionIndex,
                request.Functions[request.FunctionIndex - request.ImportedFunctionCount].Name,
                request.ExportName,
                request.Kind,
                request.IsOutwardFacing,
                ManagedBoundaryFailureDisposition.ReportAndTerminate);
    }
}
