using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AsyncJSImportCompletionEmitterTests
{
    [Fact]
    public void ResolveEmitterCompletesAndReleasesTheHandle()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var binding = CreateBinding(CliValueKind.I4);
        var emitter = new AsyncJSImportResolveEmitter(
            layouts,
            imports,
            new GeneratedFunctionWriterFactory());

        var body = ((IAsyncJSImportResolveEmitter)emitter).Emit(
            binding,
            CreateFunctionIndices());

        Assert.Contains(
            Call(imports.Resolve(RuntimeImportSymbol.HandleGet)),
            Calls(body));
        Assert.Contains(
            Call(imports.Resolve(RuntimeImportSymbol.HandleRelease)),
            Calls(body));
    }

    [Fact]
    public void ResolveEmitterCompletesAResultlessTask()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var binding = CreateBinding(CliValueKind.I4) with
        {
            Return = new JavaScriptAsyncReturn(
                JavaScriptAsyncReturnKind.Task,
                null),
        };
        var emitter = new AsyncJSImportResolveEmitter(
            new RecordingLayoutProvider(),
            imports,
            new GeneratedFunctionWriterFactory());

        var body = ((IAsyncJSImportResolveEmitter)emitter).Emit(
            binding,
            CreateFunctionIndices());

        Assert.Contains(Call(imports.Resolve(RuntimeImportSymbol.HandleRelease)), Calls(body));
    }

    [Fact]
    public void RejectEmitterStoresTheJavaScriptExceptionAndReleasesTheHandle()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var binding = CreateBinding(CliValueKind.I4);
        var emitter = new AsyncJSImportRejectEmitter(
            layouts,
            layouts,
            imports,
            new GeneratedFunctionWriterFactory());

        var body = ((IAsyncJSImportRejectEmitter)emitter).Emit(
            binding,
            CreateFunctionIndices());

        Assert.Contains(
            Call(imports.Resolve(RuntimeImportSymbol.HandleRelease)),
            Calls(body));
        Assert.Contains(WasmOpcodes.I32Constant, body);
    }

    [Fact]
    public void RejectEmitterUsesAWideExceptionReferenceForMemory64()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var emitter = new AsyncJSImportRejectEmitter(
            layouts,
            layouts,
            imports,
            new GeneratedFunctionWriterFactory());

        var body = ((IAsyncJSImportRejectEmitter)emitter).Emit(
            CreateBinding(CliValueKind.I4),
            CreateFunctionIndices());

        Assert.Contains(WasmOpcodes.I64Constant, body);
    }

    [Fact]
    public void CancelEmitterInvokesCancellationAndReleasesTheHandle()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var binding = CreateBinding(CliValueKind.I4);
        var emitter = new AsyncJSImportCancelEmitter(
            layouts,
            imports,
            new GeneratedFunctionWriterFactory());

        var body = ((IAsyncJSImportCancelEmitter)emitter).Emit(
            binding,
            CreateFunctionIndices());

        Assert.Contains(
            Call(imports.Resolve(RuntimeImportSymbol.HandleRelease)),
            Calls(body));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I8)]
    public void ResolvesNativeIntAtTargetAddressWidth(
        WasmTarget target,
        CliValueKind expectedResult)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var methods = new FakeProgram();
        var definition = methods.GetMethod(EntryKey);
        var instance = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "Task", isValueType: false),
            [],
            definition.Signature);
        var binding = new JavaScriptAsyncMethodBinding(
            EntryKey,
            new(JavaScriptAsyncReturnKind.Task,
                CliTypeIdentity.FromStackKind(CliValueKind.NativeInt)),
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
            instance,
            instance,
            instance);
        var emitter = new AsyncJSImportResolveTypeResolver(layouts);

        var type = ((IAsyncJSImportResolveTypeResolver)emitter).Resolve(binding);

        Assert.Equal(expectedResult, type.Parameters[^1]);
    }

    [Fact]
    public void ResolvesVoidAsyncCompletionWithoutAResultParameter()
    {
        var binding = CreateBinding(CliValueKind.I4) with
        {
            Return = new JavaScriptAsyncReturn(
                JavaScriptAsyncReturnKind.Task,
                null),
        };
        var emitter = new AsyncJSImportResolveTypeResolver(
            new RecordingLayoutProvider());

        var type = ((IAsyncJSImportResolveTypeResolver)emitter).Resolve(binding);

        Assert.Equal(CliValueKind.I4, Assert.Single(type.Parameters));
    }

    private static JavaScriptAsyncMethodBinding CreateBinding(CliValueKind result)
    {
        var methods = new FakeProgram();
        var definition = methods.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(result, CliValueKind.I4),
        };
        var instance = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", "Task", isValueType: false),
            [],
            definition.Signature);
        return new(
            EntryKey,
            new(JavaScriptAsyncReturnKind.Task,
                CliTypeIdentity.FromStackKind(result)),
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
            instance,
            instance,
            instance);
    }

    private static FunctionIndexResolver CreateFunctionIndices()
    {
        var methods = new FakeProgram();
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
