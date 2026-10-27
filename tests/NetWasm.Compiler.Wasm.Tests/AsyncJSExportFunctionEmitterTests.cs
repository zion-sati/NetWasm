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

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void WrapperRunsTheFinalizerSafepointFromTheSelectedImportMap(
        WasmTarget target,
        bool includeDiagnostics)
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var methods = new FakeProgram();
        var emitter = CreateWrapperEmitter(layouts, imports);
        var selection = new RuntimeImportSelection(
            WasmModuleProfile.ComponentCoreModule,
            IncludeTerminalExceptionReporter: includeDiagnostics,
            IncludeStackTrace: includeDiagnostics,
            IncludeExceptionCapture: includeDiagnostics,
            IncludeTerminalExceptionRaise: includeDiagnostics,
            IncludeFinalization: true);

        var body = emitter.Emit(
            methods.GetMethod(EntryKey),
            CreateBinding(methods, CliValueKind.I4),
            initialization: TestRuntimeInitialization.Create(512, selection),
            hasFinalizers: true,
            CreateFunctionIndices(methods));

        var selectedIndex = imports.Resolve(RuntimeImportSymbol.FinalizerSafepoint, selection);
        var fullCatalogIndex = imports.Resolve(RuntimeImportSymbol.FinalizerSafepoint);
        Assert.NotEqual(fullCatalogIndex, selectedIndex);
        Assert.Contains(
            Call(selectedIndex),
            Calls(body));
        Assert.DoesNotContain(
            Call(fullCatalogIndex),
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
            [])
        {
            ManagedThrowFunctionIndex = OptionalFunctionIndex.At(EmitterTestSupport.ManagedThrowFunction),
        };
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
    public void JavaScriptWrapperRootsStringAndArrayArgumentsUntilTheMethodReturns()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var methods = new FakeProgram();
        var stringType = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);
        var doubles = CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("f8", CliValueKind.F8));
        var jsObject = CliTypeIdentity.Named(
            Assembly, "System.Runtime.InteropServices.JavaScript", "JSObject", isValueType: false);
        var definition = methods.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
                stringType,
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                doubles,
                jsObject),
        };
        var strings = new RecordingStringArguments();
        var arrays = new RecordingArrayArguments();
        var objects = new RecordingObjectArguments();
        var emitter = CreateWrapperEmitter(layouts, imports, strings, arrays, objects);

        var body = emitter.Emit(
            definition,
            CreateBinding(methods, CliValueKind.I4),
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            CreateFunctionIndices(methods),
            interopImports: new InteropImportPlan([], default, default, default, default, default, default));

        Assert.Equal([0], strings.Parameters);
        Assert.Equal([(doubles, 2)], arrays.Parameters);
        Assert.Equal([(jsObject, 3)], objects.Parameters);
        var calls = Calls(body);
        // One argument frame plus the existing task frame. The normal path leaves
        // the argument frame; the rethrowing path unwinds it with every frame the
        // failed call left behind.
        Assert.Equal(2, calls.Count(call => call == Call(imports.Resolve(RuntimeImportSymbol.RootFrameEnter))));
        Assert.Equal(2, calls.Count(call => call == Call(imports.Resolve(RuntimeImportSymbol.RootFrameLeave))));
        Assert.Equal(1, calls.Count(call => call == Call(imports.Resolve(RuntimeImportSymbol.ExceptionFrameEnter))));
        Assert.Equal(1, calls.Count(call => call == Call(imports.Resolve(RuntimeImportSymbol.ExceptionFrameLeave))));
        Assert.Equal(1, calls.Count(call => call == Call(imports.Resolve(RuntimeImportSymbol.ExceptionFrameUnwind))));
        Assert.Contains(WasmOpcodes.ThrowRef, body);
    }

    [Fact]
    public void WrapperWithoutAJavaScriptBoundaryPassesReferencesThrough()
    {
        var imports = WasmRuntimeImports.CreateCatalog();
        var layouts = new RecordingLayoutProvider();
        var methods = new FakeProgram();
        var definition = methods.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference),
                CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false)),
        };
        var strings = new RecordingStringArguments();
        var emitter = CreateWrapperEmitter(layouts, imports, strings, new RecordingArrayArguments());

        var body = emitter.Emit(
            definition,
            CreateBinding(methods, CliValueKind.I4),
            initialization: TestRuntimeInitialization.Create(512),
            hasFinalizers: false,
            CreateFunctionIndices(methods));

        Assert.Empty(strings.Parameters);
        Assert.Equal(1, Calls(body).Count(call =>
            call == Call(imports.Resolve(RuntimeImportSymbol.RootFrameEnter))));
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
            CreateAddressInstructions(layouts),
            new JavaScriptNullableEmitter(layouts, layouts, CreateAddressInstructions(layouts)),
            new GeneratedFunctionWriterFactory());

        var body = ((IAsyncJSExportResultEmitter)emitter).Emit(binding);

        Assert.Contains(
            Call(imports.Resolve(RuntimeImportSymbol.HandleGet)),
            Calls(body));
        Assert.Contains(WasmOpcodes.I32Load, body);
    }

    // Issue #127: a JSObject result leaves as its host handle, as a synchronous
    // export's does, so the host looks the object up.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void JSObjectResultsLeaveAsTheirHostHandle(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var binding = CreateBinding(new FakeProgram(), CliValueKind.ManagedReference) with
        {
            Return = new JavaScriptAsyncReturn(JavaScriptAsyncReturnKind.Task, JSObjectType),
        };
        var emitter = new AsyncJSExportResultEmitter(
            layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            CreateAddressInstructions(layouts),
            new JavaScriptNullableEmitter(layouts, layouts, CreateAddressInstructions(layouts)),
            new GeneratedFunctionWriterFactory());

        var body = ((IAsyncJSExportResultEmitter)emitter).Emit(binding);
        var type = ((IAsyncJSExportResultTypeResolver)new AsyncJSExportResultTypeResolver(layouts)).Resolve(binding);

        Assert.Equal(CliValueKind.I4, type.Result);
        // One local holds the object; null becomes handle 0, otherwise the handle field is read.
        Assert.Equal<byte>([1, 1, target == WasmTarget.Wasm64 ? (byte)WasmValueType.I64 : (byte)WasmValueType.I32],
            body[..3]);
        Assert.Contains(WasmOpcodes.If, body);
        Assert.Contains(WasmOpcodes.Else, body);
        Assert.Equal<byte>([WasmOpcodes.I32Load, 2, 0, WasmOpcodes.End, WasmOpcodes.End], body[^5..]);
    }

    [Theory]
    [InlineData("nullable")]
    [InlineData("date")]
    [InlineData("strings")]
    public void ResultEmitterReadsAConvertedResultOnlyAtAJavaScriptBoundary(string kind)
    {
        var layouts = new RecordingLayoutProvider();
        var result = kind switch
        {
            "nullable" => CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(Assembly, "System", "Nullable`1", isValueType: true),
                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]),
            "date" => CliTypeIdentity.Named(Assembly, "System", "DateTime", isValueType: true),
            _ => CliTypeIdentity.SzArray(CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false)),
        };
        var binding = CreateBinding(new FakeProgram(), CliValueKind.I4) with
        {
            Return = new JavaScriptAsyncReturn(JavaScriptAsyncReturnKind.Task, result),
        };
        var emitter = new AsyncJSExportResultEmitter(
            layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            CreateAddressInstructions(layouts),
            new JavaScriptNullableEmitter(layouts, layouts, CreateAddressInstructions(layouts)),
            new GeneratedFunctionWriterFactory());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IAsyncJSExportResultEmitter)emitter).Emit(binding));

        Assert.Contains("only at a JavaScript boundary", exception.Message);
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
            CreateAddressInstructions(layouts),
            new JavaScriptNullableEmitter(layouts, layouts, CreateAddressInstructions(layouts)),
            new GeneratedFunctionWriterFactory());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ((IAsyncJSExportResultEmitter)emitter).Emit(binding));

        Assert.Contains("requires Task<T>", exception.Message);
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

    private static readonly CliTypeIdentity JSObjectType = CliTypeIdentity.Named(
        Assembly, "System.Runtime.InteropServices.JavaScript", "JSObject", isValueType: false);

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
        RuntimeImportCatalog imports,
        IHostCallbackStringArgumentMarshaller? strings = null,
        IHostCallbackByteArrayArgumentMarshaller? arrays = null,
        IHostCallbackHostObjectArgumentMarshaller? objects = null) => new[]
        {
            new AsyncJSExportWrapperEmitter(
                layouts,
                layouts,
                imports,
                CreateRuntimeStateInitializer(),
                new ImplicitExceptionEmitter(layouts, layouts),
                CreateReferenceComparisons(layouts),
                new GeneratedFunctionWriterFactory(),
                new JavaScriptReferenceArgumentEmitter(
                    strings ?? new RecordingStringArguments(),
                    arrays ?? new RecordingArrayArguments(),
                    objects ?? new RecordingObjectArguments()),
                EmitterTestSupport.ValueFramePlanner(layouts),
                EmitterTestSupport.ValueFrames(layouts, imports)),
        }.Cast<IAsyncJSExportWrapperEmitter>().Single();

    private static FunctionIndexResolver CreateFunctionIndices(FakeProgram methods)
    {
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey,
                new(30)),
            [],
            [],
            [])
        {
            ManagedThrowFunctionIndex = OptionalFunctionIndex.At(EmitterTestSupport.ManagedThrowFunction),
        };
        return new FunctionIndexResolver(methods, methods, indices);
    }

    private static byte[] Calls(byte[] body) => [.. body
        .Select((value, index) => (value, index))
        .Where(item => item.value == WasmOpcodes.Call && item.index + 1 < body.Length)
        .Select(item => body[item.index + 1])];

    private static byte Call(int index) => checked((byte)index);

    private sealed class RecordingStringArguments : IHostCallbackStringArgumentMarshaller
    {
        public List<int> Parameters { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            int handleParameter,
            int destination,
            int temporaryI4,
            int objectTemporary,
            InteropMarshallingTarget target,
            OptionalFunctionIndex throwFunction) => Parameters.Add(handleParameter);
    }

    private sealed class RecordingObjectArguments : IHostCallbackHostObjectArgumentMarshaller
    {
        public List<(CliTypeIdentity Type, int Parameter)> Parameters { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            CliTypeIdentity objectType,
            int handleParameter,
            int destination,
            OptionalFunctionIndex throwFunction) => Parameters.Add((objectType, handleParameter));
    }

    private sealed class RecordingArrayArguments : IHostCallbackByteArrayArgumentMarshaller
    {
        public List<(CliTypeIdentity Type, int Parameter)> Parameters { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            CliTypeIdentity arrayType,
            int handleParameter,
            int destination,
            int temporaryI4,
            int objectTemporary,
            InteropMarshallingTarget target,
            OptionalFunctionIndex throwFunction) => Parameters.Add((arrayType, handleParameter));
    }
}
