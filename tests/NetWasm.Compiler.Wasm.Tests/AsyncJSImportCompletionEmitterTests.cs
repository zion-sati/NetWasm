using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

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
        var emitter = CreateResolveEmitter(layouts, imports);

        var body = ((IAsyncJSImportResolveEmitter)emitter).Emit(
            binding,
            CreateFunctionIndices(),
            EmptyInteropImports());

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
        var emitter = CreateResolveEmitter(new RecordingLayoutProvider(), imports);

        var body = ((IAsyncJSImportResolveEmitter)emitter).Emit(
            binding,
            CreateFunctionIndices(),
            EmptyInteropImports());

        Assert.Contains(Call(imports.Resolve(RuntimeImportSymbol.HandleRelease)), Calls(body));
    }

    [Theory]
    [InlineData("string")]
    [InlineData("f64[]")]
    [InlineData("JSObject")]
    public void ResolveEmitterMarshalsAReferenceResultFromItsHandle(string kind)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var result = ReferenceResult(kind);
        var binding = CreateBinding(CliValueKind.ManagedReference) with
        {
            Return = new(JavaScriptAsyncReturnKind.Task, result),
        };
        var strings = new RecordingStringResults();
        var arrays = new RecordingArrayResults();
        var objects = new RecordingObjectResults();
        var emitter = new AsyncJSImportResolveEmitter(
            new RecordingLayoutProvider(),
            new RecordingLayoutProvider(),
            imports,
            new GeneratedFunctionWriterFactory(),
            strings,
            arrays,
            objects,
            new JavaScriptNullableEmitter(
                new RecordingLayoutProvider(), new RecordingLayoutProvider(),
                new NetWasm.Compiler.Wasm.Emission.Support.AddressInstructionEmitter(new RecordingLayoutProvider())));

        var body = ((IAsyncJSImportResolveEmitter)emitter).Emit(
            binding,
            CreateFunctionIndices(),
            EmptyInteropImports());

        // The value handle is the second parameter; the managed value lands in the
        // local after the task.
        Assert.Equal(kind == "string" ? [(1, 3)] : [], strings.Calls);
        Assert.Equal(kind == "f64[]" ? [(result, 1, 3)] : [], arrays.Calls);
        Assert.Equal(kind == "JSObject" ? [(result, 1, 3)] : [], objects.Calls);
        Assert.Contains(Call(imports.Resolve(RuntimeImportSymbol.HandleRelease)), Calls(body));
    }

    [Theory]
    [InlineData("string")]
    [InlineData("f64[]")]
    [InlineData("JSObject")]
    public void ResolvesReferenceResultsAsHandles(string kind)
    {
        var binding = CreateBinding(CliValueKind.ManagedReference) with
        {
            Return = new(JavaScriptAsyncReturnKind.Task, ReferenceResult(kind)),
        };

        var type = ((IAsyncJSImportResolveTypeResolver)new AsyncJSImportResolveTypeResolver(
            new RecordingLayoutProvider())).Resolve(binding);

        Assert.Equal([CliValueKind.I4, CliValueKind.I4], type.Parameters.ToArray());
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
    public void ResolvesANullableDateAsItsPresenceAndUnixMilliseconds()
    {
        var date = CliTypeIdentity.Named(Assembly, "System", "DateTime", isValueType: true);
        var binding = CreateBinding(CliValueKind.I4) with
        {
            Return = new JavaScriptAsyncReturn(
                JavaScriptAsyncReturnKind.Task,
                CliTypeIdentity.GenericInstantiation(
                    CliTypeIdentity.Named(Assembly, "System", "Nullable`1", isValueType: true), [date])),
        };

        var type = ((IAsyncJSImportResolveTypeResolver)new AsyncJSImportResolveTypeResolver(
            new RecordingLayoutProvider())).Resolve(binding);

        Assert.Equal([CliValueKind.I4, CliValueKind.I4, CliValueKind.F8], type.Parameters.ToArray());
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

    private static AsyncJSImportResolveEmitter CreateResolveEmitter(
        RecordingLayoutProvider layouts,
        RuntimeImportCatalog imports) => new(
        layouts,
        layouts,
        imports,
        new GeneratedFunctionWriterFactory(),
        new RecordingStringResults(),
        new RecordingArrayResults(),
        new RecordingObjectResults(),
        new JavaScriptNullableEmitter(layouts, layouts, new NetWasm.Compiler.Wasm.Emission.Support.AddressInstructionEmitter(layouts)));

    private static InteropImportPlan EmptyInteropImports() =>
        new InteropImportPlan([], default, default, default, default, default, default);

    private static CliTypeIdentity ReferenceResult(string kind) => kind switch
    {
        "string" => CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false),
        "f64[]" => CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("f8", CliValueKind.F8)),
        _ => CliTypeIdentity.Named(
            Assembly, "System.Runtime.InteropServices.JavaScript", "JSObject", isValueType: false),
    };

    private sealed class RecordingStringResults : IHostCallbackStringArgumentMarshaller
    {
        public List<(int Handle, int Destination)> Calls { get; } = [];

        public void Emit(IWasmInstructionWriter code, int handleParameter, int destination,
            int temporaryI4, int objectTemporary, InteropMarshallingTarget target,
            OptionalFunctionIndex throwFunction) =>
            Calls.Add((handleParameter, destination));
    }

    private sealed class RecordingArrayResults : IHostCallbackByteArrayArgumentMarshaller
    {
        public List<(CliTypeIdentity Type, int Handle, int Destination)> Calls { get; } = [];

        public void Emit(IWasmInstructionWriter code, CliTypeIdentity arrayType, int handleParameter,
            int destination, int temporaryI4, int objectTemporary, InteropMarshallingTarget target,
            OptionalFunctionIndex throwFunction) =>
            Calls.Add((arrayType, handleParameter, destination));
    }

    private sealed class RecordingObjectResults : IHostCallbackHostObjectArgumentMarshaller
    {
        public List<(CliTypeIdentity Type, int Handle, int Destination)> Calls { get; } = [];

        public void Emit(IWasmInstructionWriter code, CliTypeIdentity objectType, int handleParameter,
            int destination,
            OptionalFunctionIndex throwFunction) => Calls.Add((objectType, handleParameter, destination));
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
