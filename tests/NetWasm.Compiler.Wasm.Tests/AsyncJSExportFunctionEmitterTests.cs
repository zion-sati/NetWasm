using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AsyncJSExportFunctionEmitterTests
{
    [Fact]
    public void WrapperEmitterCreatesAHandleForTheTask()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var methods = new FakeProgram();
        var binding = CreateBinding(methods, CliValueKind.I4);
        var emitter = CreateWrapperEmitter(layouts, imports);

        var body = ((IAsyncJSExportWrapperEmitter)emitter).Emit(
            methods.GetMethod(EntryKey),
            binding,
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            CreateFunctionIndices(methods));

        Assert.Contains(
            Call(imports.Resolve(RuntimeImportSymbol.HandleNew)),
            Calls(body));
    }

    [Fact]
    public void ValueTaskWrapperUsesAValueFrameAndConvertsToTask()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var methods = new FakeProgram();
        var definition = methods.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.Named(
                    Assembly,
                    "System.Threading.Tasks",
                    "ValueTask`1",
                    isValueType: true,
                    CliValueKind.ValueType),
                CliTypeIdentity.FromStackKind(CliValueKind.I4)),
        };
        var asTask = new MethodInstanceModel(
            definition,
            definition.Signature.ReturnSignatureType,
            [],
            definition.Signature);
        var binding = CreateBinding(methods, CliValueKind.I4) with
        {
            Return = new(
                JavaScriptAsyncReturnKind.ValueTask,
                CliTypeIdentity.FromStackKind(CliValueKind.I4)),
            ValueTaskAsTask = asTask,
        };
        var emitter = CreateWrapperEmitter(layouts, imports);

        var body = emitter.Emit(
            definition,
            binding,
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            CreateFunctionIndices(methods));

        Assert.Contains(
            Call(imports.Resolve(RuntimeImportSymbol.ValueFrameEnter)),
            Calls(body));
        Assert.Contains(
            Call(imports.Resolve(RuntimeImportSymbol.ValueFrameLeave)),
            Calls(body));
    }

    [Fact]
    public void WrapperRunsTheFinalizerSafepointWhenFinalizersAreReachable()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var methods = new FakeProgram();
        var emitter = CreateWrapperEmitter(layouts, imports);

        var body = emitter.Emit(
            methods.GetMethod(EntryKey),
            CreateBinding(methods, CliValueKind.I4),
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: true,
            CreateFunctionIndices(methods));

        Assert.Contains(
            Call(imports.Resolve(RuntimeImportSymbol.FinalizerSafepoint)),
            Calls(body));
    }

    [Fact]
    public void ProcessWrapperMaterializesArgumentsInsideTheGuest()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var methods = new FakeProgram();
        var definition = methods.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference),
        };
        var binding = CreateBinding(methods, CliValueKind.I4) with
        {
            Method = definition.Key,
        };
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty
                .Add(EntryKey, new(30))
                .Add(StringMethodKey, new(31)),
            [],
            [],
            []);
        var emitter = CreateWrapperEmitter(layouts, imports);

        var body = emitter.Emit(
            definition,
            binding,
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            new FunctionIndexResolver(methods, methods, indices),
            StringMethodKey);

        Assert.True(body.AsSpan().IndexOf([
            WasmOpcodes.Call,
            (byte)31,
            WasmOpcodes.Call,
            (byte)30,
        ]) >= 0);
    }

    [Fact]
    public void StatusEmitterReadsTheStatusFieldThroughTheHandle()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var binding = CreateBinding(new FakeProgram(), CliValueKind.I4);
        var emitter = new AsyncJSExportStatusEmitter(
            layouts,
            imports,
            new GeneratedFunctionWriterFactory());

        var body = ((IAsyncJSExportStatusEmitter)emitter).Emit(binding);

        Assert.Contains(
            Call(imports.Resolve(RuntimeImportSymbol.HandleGet)),
            Calls(body));
        Assert.Contains(WasmOpcodes.I32Load, body);
    }

    [Fact]
    public void ResultEmitterLoadsTheTaskResultThroughTheHandle()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var binding = CreateBinding(new FakeProgram(), CliValueKind.I4);
        var emitter = new AsyncJSExportResultEmitter(
            layouts,
            layouts,
            imports,
            new GeneratedFunctionWriterFactory());

        var body = ((IAsyncJSExportResultEmitter)emitter).Emit(binding);

        Assert.Contains(
            Call(imports.Resolve(RuntimeImportSymbol.HandleGet)),
            Calls(body));
        Assert.Contains(WasmOpcodes.I32Load, body);
    }

    [Fact]
    public void ResultEmitterRejectsAResultlessTask()
    {
        var layouts = new RecordingLayoutProvider();
        var binding = CreateBinding(new FakeProgram(), CliValueKind.I4) with
        {
            Return = new JavaScriptAsyncReturn(
                JavaScriptAsyncReturnKind.Task,
                null),
        };
        var emitter = new AsyncJSExportResultEmitter(
            layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            new GeneratedFunctionWriterFactory());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IAsyncJSExportResultEmitter)emitter).Emit(binding));

        Assert.Contains("requires Task<T>", exception.Message);
    }

    [Fact]
    public void CompletionEmitterReleasesTheHandle()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var emitter = new AsyncJSExportCompletionEmitter(
            imports,
            new GeneratedFunctionWriterFactory());

        var body = ((IAsyncJSExportCompletionEmitter)emitter).Emit();

        Assert.Contains(
            Call(imports.Resolve(RuntimeImportSymbol.HandleRelease)),
            Calls(body));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I8)]
    public void ResultTypeResolverNormalizesNativeInt(
        WasmTarget target,
        CliValueKind expectedResult)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var binding = CreateBinding(new FakeProgram(), CliValueKind.NativeInt);
        var emitter = new AsyncJSExportResultTypeResolver(layouts);

        var type = ((IAsyncJSExportResultTypeResolver)emitter).Resolve(binding);

        Assert.Equal(expectedResult, type.Result);
    }

    private static JavaScriptAsyncMethodBinding CreateBinding(
        FakeProgram methods,
        CliValueKind result)
    {
        var definition = methods.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(result, CliValueKind.I4),
        };
        var instance = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "Task", isValueType: false),
            [],
            definition.Signature);
        var fieldDefinition = methods.GetField(InstanceFieldKey);
        var field = new FieldInstanceModel(
            fieldDefinition,
            CliTypeIdentity.Named(Assembly, "Test", "Task", isValueType: false),
            CliTypeIdentity.FromStackKind(result));
        return new JavaScriptAsyncMethodBinding(
            EntryKey,
            new(JavaScriptAsyncReturnKind.Task,
                CliTypeIdentity.FromStackKind(result)),
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
            instance,
            instance,
            instance)
        {
            StatusField = field,
            ResultField = field,
        };
    }

    private static IAsyncJSExportWrapperEmitter CreateWrapperEmitter(
        RecordingLayoutProvider layouts,
        RuntimeImportCatalog imports) => new[]
        {
            new AsyncJSExportWrapperEmitter(
                layouts,
                layouts,
                imports,
                CreateRuntimeStateInitializer(layouts, imports),
                new ImplicitExceptionEmitter(layouts, layouts, 7),
                CreateReferenceComparisons(layouts),
                new GeneratedFunctionWriterFactory()),
        }.Cast<IAsyncJSExportWrapperEmitter>().Single();

    private static FunctionIndexResolver CreateFunctionIndices(FakeProgram methods)
    {
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey,
                new(30)),
            [],
            [],
            []);
        return new FunctionIndexResolver(methods, methods, indices);
    }

    private static byte[] Calls(byte[] body) => [.. body
        .Select((value, index) => (value, index))
        .Where(item => item.value == WasmOpcodes.Call && item.index + 1 < body.Length)
        .Select(item => body[item.index + 1])];

    private static byte Call(int index) => checked((byte)index);
}
