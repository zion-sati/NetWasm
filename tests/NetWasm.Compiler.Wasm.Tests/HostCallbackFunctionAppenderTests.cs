using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class HostCallbackFunctionAppenderTests
{
    [Fact]
    public void AppendsFunctionIndexBodyAndBoundaryPolicy()
    {
        var callback = CreateCallback();
        var functions = new List<WasmFunctionDefinition>();
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var boundaries = new List<ManagedBoundaryPlanEntry>();
        var bodyEmitter = new FixedFunctionEmitter();
        IHostCallbackFunctionAppender appender = new[]
        {
            new HostCallbackFunctionAppender(
                bodyEmitter,
                new FixedFunctionTypeResolver(),
                new FixedBoundaryBuilder()),
        }.Cast<IHostCallbackFunctionAppender>().Single();

        appender.Append(
            functions,
            4,
            indices,
            callback,
            EmptyFunctionIndices(),
            TestRuntimeInitialization.Create(256),
            EmptyInteropImports(),
            CoreImports,
            boundaries);

        Assert.Single(functions);
        Assert.Equal("callback", functions[0].Name);
        Assert.Equal(4, indices["callback"]);
        Assert.Equal([7], functions[0].Body);
        Assert.Equal(256, bodyEmitter.StaticDataEnd);
        Assert.Equal(ManagedBoundaryKind.HostCallback, Assert.Single(boundaries).Kind);
    }

    [Fact]
    public void RejectsMissingInputsAndNegativeValues()
    {
        var appender = CreateAppender();
        var functions = new List<WasmFunctionDefinition>();
        var indices = new Dictionary<string, int>();
        var callback = CreateCallback();
        var functionIndices = EmptyFunctionIndices();
        var imports = EmptyInteropImports();
        var boundaries = new List<ManagedBoundaryPlanEntry>();

        Assert.Throws<ArgumentNullException>(() => appender.Append(
            null!, 0, indices, callback, functionIndices, TestRuntimeInitialization.Create(0), imports, CoreImports, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(
            functions, -1, indices, callback, functionIndices, TestRuntimeInitialization.Create(0), imports, CoreImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, null!, callback, functionIndices, TestRuntimeInitialization.Create(0), imports, CoreImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, null!, functionIndices, TestRuntimeInitialization.Create(0), imports, CoreImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, callback, null!, TestRuntimeInitialization.Create(0), imports, CoreImports, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(
            functions, 0, indices, callback, functionIndices, TestRuntimeInitialization.Create(-1), imports, CoreImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, callback, functionIndices, TestRuntimeInitialization.Create(0), null!, CoreImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, callback, functionIndices, TestRuntimeInitialization.Create(0), imports, CoreImports, null!));
    }

    private static IHostCallbackFunctionAppender CreateAppender() => new[]
    {
        new HostCallbackFunctionAppender(
            new FixedFunctionEmitter(),
            new FixedFunctionTypeResolver(),
            new FixedBoundaryBuilder()),
    }.Cast<IHostCallbackFunctionAppender>().Single();

    private static HostCallbackDeclaration CreateCallback()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EmitterTestSupport.EntryKey);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "Tests",
                "Callback",
                isValueType: false),
            [],
            method.Signature);
        return new(EmitterTestSupport.EntryKey, 0, instance, "callback");
    }

    private static FunctionIndexMap EmptyFunctionIndices() => new(
        [],
        [],
        ImmutableDictionary<string, WasmFunctionIndex>.Empty,
        []);

    private static InteropImportPlan EmptyInteropImports() => new(
        [], default, default, default, default, default, default);

    private static RuntimeImportSelection CoreImports =>
        new(WasmModuleProfile.CoreApplication, true);

    private sealed class FixedFunctionEmitter : IHostCallbackFunctionEmitter
    {
        public int? StaticDataEnd { get; private set; }

        public byte[] Emit(
            HostCallbackDeclaration callback,
            FunctionIndexMap functionIndices,
            RuntimeInitializationPlan initialization,
            InteropImportPlan interopImports,
            RuntimeImportSelection runtimeImportSelection)
        {
            StaticDataEnd = initialization.StaticDataEnd;
            return [7];
        }
    }

    private sealed class FixedFunctionTypeResolver : IHostCallbackFunctionTypeResolver
    {
        public WasmFunctionType Resolve(MethodInstanceModel invoke) =>
            WasmFunctionType.Create(CliValueKind.I4, CliValueKind.I4);
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
                ManagedBoundaryFailureDisposition.PropagateManagedException);
    }
}
