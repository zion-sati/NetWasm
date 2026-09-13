using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ComponentFunctionAppenderTests
{
    [Fact]
    public void AppendsArtifactsAndClassifiesEveryComponentBoundary()
    {
        var components = new FixedComponentBoundaryEmitter();
        var boundaries = new RecordingBoundaryBuilder();
        var appender = new[]
        {
            new ComponentFunctionAppender(components, boundaries),
        }.Cast<IComponentFunctionAppender>().Single();
        var functions = new List<WasmFunctionDefinition>
        {
            new("managed", WasmFunctionType.Create(CliValueKind.Void), []),
        };
        var exports = new List<WasmExport>();
        var entries = new List<ManagedBoundaryPlanEntry>();
        var request = EmitterTestSupport.CreateEmissionRequest();

        var runtimeFeatures = appender.Append(
            functions,
            exports,
            entries,
            request,
            WasmTarget.Wasm32,
            TestRuntimeInitialization.Create(128),
            2,
            ImmutableDictionary<string, int>.Empty,
            EmitterTestSupport.CreateFunctionIndexResolver());

        Assert.Equal(4, functions.Count);
        Assert.Equal(4, exports.Count);
        Assert.Equal(
            [ManagedBoundaryKind.ComponentReallocate,
                ManagedBoundaryKind.ComponentInitialize,
                ManagedBoundaryKind.ComponentPostReturn],
            boundaries.Requests.Select(item => item.Kind));
        Assert.Equal(3, entries.Count);
        Assert.Same(request, components.Request);
        Assert.Equal(128, components.StaticDataEnd);
        Assert.Equal(2, components.ImportCount);
        Assert.Equal(1, components.DefinedFunctionCount);
        Assert.Equal([NetWasmRuntimeFeatureIds.LocalTime], runtimeFeatures.ToArray());
    }

    [Fact]
    public void RejectsMissingCollectionsRequestIndicesAndNegativeValues()
    {
        var appender = CreateAppender();
        var functions = new List<WasmFunctionDefinition>();
        var exports = new List<WasmExport>();
        var entries = new List<ManagedBoundaryPlanEntry>();
        var request = EmitterTestSupport.CreateEmissionRequest();
        var indices = ImmutableDictionary<string, int>.Empty;
        var functionIndices = EmitterTestSupport.CreateFunctionIndexResolver();

        Assert.Throws<ArgumentNullException>(() => appender.Append(
            null!, exports, entries, request, WasmTarget.Wasm32, TestRuntimeInitialization.Create(0), 0, indices,
            functionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, null!, entries, request, WasmTarget.Wasm32, TestRuntimeInitialization.Create(0), 0, indices,
            functionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, exports, null!, request, WasmTarget.Wasm32, TestRuntimeInitialization.Create(0), 0, indices,
            functionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, exports, entries, null!, WasmTarget.Wasm32, TestRuntimeInitialization.Create(0), 0, indices,
            functionIndices));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(
            functions, exports, entries, request, WasmTarget.Wasm32, TestRuntimeInitialization.Create(-1), 0, indices,
            functionIndices));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(
            functions, exports, entries, request, WasmTarget.Wasm32, TestRuntimeInitialization.Create(0), -1, indices,
            functionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, exports, entries, request, WasmTarget.Wasm32, TestRuntimeInitialization.Create(0), 0, null!,
            functionIndices));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, exports, entries, request, WasmTarget.Wasm32, TestRuntimeInitialization.Create(0), 0, indices,
            null!));
    }

    private static IComponentFunctionAppender CreateAppender() => new[]
    {
        new ComponentFunctionAppender(
            new FixedComponentBoundaryEmitter(),
            new RecordingBoundaryBuilder()),
    }.Cast<IComponentFunctionAppender>().Single();

    private sealed class FixedComponentBoundaryEmitter : IComponentBoundaryEmitter
    {
        public WasmEmissionRequest? Request { get; private set; }
        public int StaticDataEnd { get; private set; }
        public int ImportCount { get; private set; }
        public int DefinedFunctionCount { get; private set; }

        public ComponentBoundaryArtifacts Emit(
            WasmEmissionRequest request,
            WasmTarget target,
            RuntimeInitializationPlan initialization,
            int importedFunctionCount,
            int definedFunctionCount,
            IReadOnlyDictionary<string, int> managedExportIndices,
            IFunctionIndexResolver functionIndices)
        {
            Request = request;
            StaticDataEnd = initialization.StaticDataEnd;
            ImportCount = importedFunctionCount;
            DefinedFunctionCount = definedFunctionCount;
            return new(
                [
                    Function("component.realloc"),
                    Function("component.initialize"),
                    Function("component.post-return.read"),
                ],
                [
                    new("memory", 0, WasmExportKind.Memory),
                    new("realloc", importedFunctionCount + definedFunctionCount,
                        WasmExportKind.Function),
                    new("initialize", importedFunctionCount + definedFunctionCount + 1,
                        WasmExportKind.Function),
                    new("post-return", importedFunctionCount + definedFunctionCount + 2,
                        WasmExportKind.Function),
                ],
                [NetWasmRuntimeFeatureIds.LocalTime]);
        }

        private static WasmFunctionDefinition Function(string name) =>
            new(name, WasmFunctionType.Create(CliValueKind.Void), []);
    }

    private sealed class RecordingBoundaryBuilder : IManagedBoundaryPlanBuilder
    {
        public List<ManagedBoundaryPlanBuildRequest> Requests { get; } = [];

        public ManagedBoundaryPlanEntry Build(ManagedBoundaryPlanBuildRequest request)
        {
            Requests.Add(request);
            return new(
                request.FunctionIndex,
                request.Functions[request.FunctionIndex - request.ImportedFunctionCount].Name,
                request.ExportName,
                request.Kind,
                request.IsOutwardFacing,
                ManagedBoundaryFailureDisposition.PropagateManagedException);
        }
    }
}
