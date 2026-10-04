using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RequestedExportFunctionAppenderTests
{
    [Theory]
    [InlineData(true, WasmModuleProfile.CoreApplication,
        ManagedBoundaryKind.AsynchronousExportStart)]
    [InlineData(true, WasmModuleProfile.ComponentCoreModule,
        ManagedBoundaryKind.ComponentAdapter)]
    [InlineData(false, WasmModuleProfile.CoreApplication,
        ManagedBoundaryKind.SynchronousExport)]
    [InlineData(false, WasmModuleProfile.ComponentCoreModule,
        ManagedBoundaryKind.ComponentAdapter)]
    public void AppendsAsyncOrSyncExportWithProfileBoundary(
        bool asynchronous,
        WasmModuleProfile profile,
        ManagedBoundaryKind expectedKind)
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EmitterTestSupport.EntryKey) with
        {
            JSExport = asynchronous ? new("run") : null,
        };
        var methodInstance = CreateInstance(method);
        var binding = CreateBinding(method);
        var asyncHelpers = new RecordingAsyncHelperAppender();
        var wrappers = new RecordingAsyncWrapperEmitter();
        var entries = new RecordingEntryPointEmitter();
        IRequestedExportFunctionAppender appender = new[]
        {
            new RequestedExportFunctionAppender(
                new OutwardMethodFunctionAppender(
                    wrappers,
                    asyncHelpers,
                    entries,
                    new FixedFunctionTypeResolver(),
                    new FixedBoundaryBuilder(),
                    WasmRuntimeImports.CreateCatalog())),
        }.Cast<IRequestedExportFunctionAppender>().Single();
        var functions = new List<WasmFunctionDefinition>();
        var indices = new Dictionary<string, int>();
        var helperIndices = new Dictionary<string, int>();
        var boundaries = new List<ManagedBoundaryPlanEntry>();
        IReadOnlyDictionary<EntityKey, JavaScriptAsyncMethodBinding> bindings =
            asynchronous
                ? ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty.Add(
                    method.Key,
                    binding)
                : ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty;

        appender.Append(
            functions,
            3,
            indices,
            helperIndices,
            "run",
            methodInstance,
            bindings,
            TestRuntimeInitialization.Create(256, new(profile, false, IncludeExceptionCapture: asynchronous)),
            true,
            profile,
            new FixedFunctionIndexResolver(),
            EmptyInteropImports(),
            boundaries);

        Assert.Equal(3, indices["run"]);
        Assert.Equal("netwasm.export.run", Assert.Single(functions).Name);
        Assert.Equal(expectedKind, Assert.Single(boundaries).Kind);
        Assert.Equal(asynchronous ? 1 : 0, wrappers.Count);
        Assert.Equal(asynchronous ? 1 : 0, asyncHelpers.Count);
        Assert.Equal(asynchronous ? 0 : 1, entries.Count);
        if (!asynchronous)
        {
            Assert.Equal(profile == WasmModuleProfile.CoreApplication,
                entries.ReportTerminalExceptions);
        }
    }

    [Fact]
    public void JavaScriptBoundaryOverridesComponentModuleExportBoundary()
    {
        var method = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey);
        var entries = new RecordingEntryPointEmitter();
        var appender = new RequestedExportFunctionAppender(
            new OutwardMethodFunctionAppender(
                new RecordingAsyncWrapperEmitter(),
                new RecordingAsyncHelperAppender(),
                entries,
                new FixedFunctionTypeResolver(),
                new FixedBoundaryBuilder(),
                WasmRuntimeImports.CreateCatalog()));
        var boundaries = new List<ManagedBoundaryPlanEntry>();
        var interop = EmptyInteropImports() with
        {
            UseJavaScriptExportBoundary = true,
        };

        appender.Append(
            [],
            0,
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            "run",
            CreateInstance(method),
            ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty,
            TestRuntimeInitialization.Create(0),
            false,
            WasmModuleProfile.ComponentCoreModule,
            new FixedFunctionIndexResolver(),
            interop,
            boundaries);

        Assert.Equal(ManagedBoundaryKind.SynchronousExport,
            Assert.Single(boundaries).Kind);
        Assert.True(entries.ReportTerminalExceptions);
    }

    [Fact]
    public void RejectsMissingInputsInvalidNameAndNegativeValues()
    {
        var method = new FakeProgram().GetMethod(EmitterTestSupport.EntryKey);
        var methodInstance = CreateInstance(method);
        var appender = CreateAppender(method);
        var functions = new List<WasmFunctionDefinition>();
        var indices = new Dictionary<string, int>();
        var helpers = new Dictionary<string, int>();
        var bindings = ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Empty;
        var functionIndices = new FixedFunctionIndexResolver();
        var interopImports = EmptyInteropImports();
        var boundaries = new List<ManagedBoundaryPlanEntry>();

        Assert.Throws<ArgumentNullException>(() => appender.Append(
            null!, 0, indices, helpers, "run", methodInstance, bindings, TestRuntimeInitialization.Create(0), false,
            WasmModuleProfile.CoreApplication, functionIndices, interopImports, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(
            functions, -1, indices, helpers, "run", methodInstance, bindings, TestRuntimeInitialization.Create(0), false,
            WasmModuleProfile.CoreApplication, functionIndices, interopImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, null!, helpers, "run", methodInstance, bindings, TestRuntimeInitialization.Create(0), false,
            WasmModuleProfile.CoreApplication, functionIndices, interopImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, null!, "run", methodInstance, bindings, TestRuntimeInitialization.Create(0), false,
            WasmModuleProfile.CoreApplication, functionIndices, interopImports, boundaries));
        Assert.Throws<ArgumentException>(() => appender.Append(
            functions, 0, indices, helpers, " ", methodInstance, bindings, TestRuntimeInitialization.Create(0), false,
            WasmModuleProfile.CoreApplication, functionIndices, interopImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, helpers, "run", null!, bindings, TestRuntimeInitialization.Create(0), false,
            WasmModuleProfile.CoreApplication, functionIndices, interopImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, helpers, "run", methodInstance, null!, TestRuntimeInitialization.Create(0), false,
            WasmModuleProfile.CoreApplication, functionIndices, interopImports, boundaries));
        Assert.Throws<ArgumentOutOfRangeException>(() => appender.Append(
            functions, 0, indices, helpers, "run", methodInstance, bindings, TestRuntimeInitialization.Create(-1), false,
            WasmModuleProfile.CoreApplication, functionIndices, interopImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, helpers, "run", methodInstance, bindings, TestRuntimeInitialization.Create(0), false,
            WasmModuleProfile.CoreApplication, null!, interopImports, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, helpers, "run", methodInstance, bindings,
            TestRuntimeInitialization.Create(0), false,
            WasmModuleProfile.CoreApplication, functionIndices, null!, boundaries));
        Assert.Throws<ArgumentNullException>(() => appender.Append(
            functions, 0, indices, helpers, "run", methodInstance, bindings, TestRuntimeInitialization.Create(0), false,
            WasmModuleProfile.CoreApplication, functionIndices, interopImports, null!));
    }

    private static IRequestedExportFunctionAppender CreateAppender(
        MethodDefinitionModel method) => new[]
    {
        new RequestedExportFunctionAppender(
            new OutwardMethodFunctionAppender(
                new RecordingAsyncWrapperEmitter(),
                new RecordingAsyncHelperAppender(),
                new RecordingEntryPointEmitter(),
                new FixedFunctionTypeResolver(),
                new FixedBoundaryBuilder(),
                WasmRuntimeImports.CreateCatalog())),
    }.Cast<IRequestedExportFunctionAppender>().Single();

    private static InteropImportPlan EmptyInteropImports() => new(
        [],
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing);

    private static MethodInstanceModel CreateInstance(MethodDefinitionModel method) =>
        new(
            method,
            CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "Tests",
                "EntryPoint",
                isValueType: false),
            [],
            method.Signature);

    private static JavaScriptAsyncMethodBinding CreateBinding(
        MethodDefinitionModel method)
    {
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(
                EmitterTestSupport.Assembly,
                "Tests",
                "TaskSource",
                isValueType: false),
            [],
            method.Signature);
        return new(
            method.Key,
            new(JavaScriptAsyncReturnKind.Task, null),
            instance.DeclaringType,
            instance,
            instance,
            instance)
        { GetVoidResult = instance };
    }

    private sealed class RecordingAsyncWrapperEmitter : IAsyncJSExportWrapperEmitter
    {
        public int Count { get; private set; }

        public byte[] Emit(
            MethodDefinitionModel method,
            JavaScriptAsyncMethodBinding binding,
            RuntimeInitializationPlan initialization,
            bool hasFinalizers,
            IFunctionIndexResolver functionIndices,
            EntityKey? argumentFactory)
        {
            Count++;
            return [1];
        }
    }

    private sealed class RecordingAsyncHelperAppender : IAsyncJSExportHelperAppender
    {
        public int Count { get; private set; }

        public void Append(
            IList<WasmFunctionDefinition> functions,
            int importCount,
            IDictionary<string, int> indices,
            JavaScriptAsyncMethodBinding binding,
            ManagedAsyncBoundaryNames names,
            ManagedAsyncBoundaryKinds kinds,
            ICollection<ManagedBoundaryPlanEntry> boundaryEntries,
            AsyncTaskCompletionPlan completion) => Count++;
    }

    private sealed class RecordingEntryPointEmitter : IEntryPointEmitter
    {
        public int Count { get; private set; }
        public bool ReportTerminalExceptions { get; private set; }

        public byte[] Emit(
            MethodDefinitionModel entryPoint,
            RuntimeInitializationPlan initialization,
            bool hasFinalizers,
            IFunctionIndexResolver functionIndices,
            bool reportTerminalExceptions,
            EntityKey? argumentFactory)
        {
            Count++;
            ReportTerminalExceptions = reportTerminalExceptions;
            return [2];
        }
    }

    private sealed class FixedFunctionTypeResolver : IManagedMethodFunctionTypeResolver
    {
        public WasmFunctionType Resolve(MethodDefinitionModel method) =>
            WasmFunctionType.Create(CliValueKind.I4);

        public WasmFunctionType Resolve(MethodInstanceModel method) =>
            WasmFunctionType.Create(CliValueKind.I4);
    }

    private sealed class FixedFunctionIndexResolver : IFunctionIndexResolver
    {
        public int Resolve(EntityKey method) => 0;
        public int Resolve(string method) => 0;
        public int Resolve(MethodInstanceModel method) => 0;
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
