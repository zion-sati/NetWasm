using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class HostCallbackFunctionEmitterTests
{
    [Fact]
    public void CallbackAbiUsesHandlesForStringAndByteArrayParameters()
    {
        var method = new FakeProgram().GetMethod(EntryKey) with
        {
            Signature = new MethodSignatureModel(
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                [
                    CliTypeIdentity.Primitive(
                        "string",
                        CliValueKind.ManagedReference,
                        isValueType: false),
                    CliTypeIdentity.SzArray(
                        CliTypeIdentity.Primitive("u1", CliValueKind.I4)),
                ]),
        };
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Callback", isValueType: false),
            [],
            method.Signature);

        var type = CreateFunctionTypes().Resolve(instance);

        Assert.True(type.Parameters.SequenceEqual(
            [CliValueKind.I4, CliValueKind.I4, CliValueKind.I4]));
        Assert.Equal(CliValueKind.I4, type.Result);
    }

    [Fact]
    public void EmitsStringByteArrayAndScalarCallbackArgumentsThroughCollaborators()
    {
        var stringType = CliTypeIdentity.Primitive(
            "string",
            CliValueKind.ManagedReference,
            isValueType: false);
        var byteArrayType = CliTypeIdentity.SzArray(
            CliTypeIdentity.Primitive("u1", CliValueKind.I4));
        var method = new FakeProgram().GetMethod(EntryKey) with
        {
            Signature = new MethodSignatureModel(
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                [
                    stringType,
                    byteArrayType,
                    CliTypeIdentity.FromStackKind(CliValueKind.I8),
                ]),
        };
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Callback", isValueType: false),
            [],
            method.Signature);
        var callback = new HostCallbackDeclaration(EntryKey, 0, instance, "callback");
        var stringMarshaller = new RecordingStringMarshaller();
        var byteMarshaller = new RecordingByteArrayMarshaller();
        var runtime = new RecordingRuntimeInitializer();
        var layouts = new RecordingLayoutProvider();
        var terminalBoundary = new PassthroughTerminalBoundary();
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var runtimeImportSelection = new RuntimeImportSelection(
            WasmModuleProfile.CoreApplication,
            IncludeTerminalExceptionReporter: true);
        IHostCallbackFunctionEmitter emitter = new[]
        {
            new HostCallbackFunctionEmitter(
                layouts,
                runtimeImports,
                runtime,
                new ImplicitExceptionEmitter(layouts, layouts),
                new JavaScriptReferenceArgumentEmitter(
                    stringMarshaller, byteMarshaller, new RecordingHostObjectMarshaller()),
                EmitterTestSupport.ValueFramePlanner(layouts),
                EmitterTestSupport.ValueFrames(layouts, runtimeImports),
                terminalBoundary,
                new AddressInstructionEmitter(layouts),
                new GeneratedFunctionWriterFactory()),
        }.Cast<IHostCallbackFunctionEmitter>().Single();
        var indices = new FunctionIndexMap(
            [],
            [],
            ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
                instance.DeclaringType.CanonicalName,
                new WasmFunctionIndex(30)),
            [])
        {
            ManagedThrowFunctionIndex = OptionalFunctionIndex.At(EmitterTestSupport.ManagedThrowFunction),
        };

        Assert.Equal(4, CreateFunctionTypes().Resolve(instance).Parameters.Length);

        var body = emitter.Emit(
            callback,
            indices,
            TestRuntimeInitialization.Create(256),
            new InteropImportPlan(
                [],
                OptionalFunctionIndex.At(1),
                OptionalFunctionIndex.At(2),
                OptionalFunctionIndex.Missing,
                OptionalFunctionIndex.Missing,
                OptionalFunctionIndex.At(3),
                OptionalFunctionIndex.At(4)),
            runtimeImportSelection);

        Assert.True(runtime.Called);
        Assert.True(stringMarshaller.Called);
        Assert.True(byteMarshaller.Called);
        Assert.Equal(runtimeImports.Resolve(
            RuntimeImportSymbol.ManagedTerminalExceptionReport,
            runtimeImportSelection), terminalBoundary.ReportFunctionIndex);
        Assert.Contains(WasmOpcodes.Call, body);
        Assert.Contains(WasmOpcodes.LocalSet, body);
    }

    // A callback takes and returns what a synchronous export does: every converted
    // argument is rooted while the next allocates, and a JSObject result leaves as
    // its host handle.
    [Fact]
    public void RootsEveryReferenceArgumentAndReturnsAJSObjectAsItsHandle()
    {
        var stringType = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);
        var objectType = CliTypeIdentity.Named(
            Assembly, "System.Runtime.InteropServices.JavaScript", "JSObject", isValueType: false);
        var method = new FakeProgram().GetMethod(EntryKey) with
        {
            Signature = new MethodSignatureModel(objectType, [stringType, stringType, objectType]),
        };
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Callback", isValueType: false),
            [],
            method.Signature);
        var strings = new RecordingStringMarshaller();
        var objects = new RecordingHostObjectMarshaller();
        var layouts = new RecordingLayoutProvider();
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var selection = new RuntimeImportSelection(WasmModuleProfile.CoreApplication,
            IncludeTerminalExceptionReporter: true);
        IHostCallbackFunctionEmitter emitter = new[]
        {
            new HostCallbackFunctionEmitter(
                layouts, runtimeImports, new RecordingRuntimeInitializer(), new ImplicitExceptionEmitter(layouts, layouts),
                new JavaScriptReferenceArgumentEmitter(strings, new RecordingByteArrayMarshaller(), objects),
                EmitterTestSupport.ValueFramePlanner(layouts),
                EmitterTestSupport.ValueFrames(layouts, runtimeImports),
                new PassthroughTerminalBoundary(),
                new AddressInstructionEmitter(layouts), new GeneratedFunctionWriterFactory()),
        }.Cast<IHostCallbackFunctionEmitter>().Single();
        var indices = new FunctionIndexMap([], [],
            ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(instance.DeclaringType.CanonicalName, new(30)), [])
        {
            ManagedThrowFunctionIndex = OptionalFunctionIndex.At(EmitterTestSupport.ManagedThrowFunction),
        };

        var type = CreateFunctionTypes().Resolve(instance);
        var body = emitter.Emit(new HostCallbackDeclaration(EntryKey, 0, instance, "callback"), indices,
            TestRuntimeInitialization.Create(256), new InteropImportPlan([], OptionalFunctionIndex.At(1),
                OptionalFunctionIndex.At(2), OptionalFunctionIndex.Missing, OptionalFunctionIndex.Missing,
                OptionalFunctionIndex.At(3), OptionalFunctionIndex.At(4)), selection);

        Assert.Equal([CliValueKind.I4, CliValueKind.I4, CliValueKind.I4, CliValueKind.I4], type.Parameters.ToArray());
        Assert.Equal(CliValueKind.I4, type.Result);
        Assert.Equal(2, strings.Calls);
        Assert.True(objects.Called);
        var calls = body.Select((value, index) => (value, index))
            .Where(item => item.value == WasmOpcodes.Call && item.index + 1 < body.Length)
            .Select(item => (int)body[item.index + 1]).ToArray();
        Assert.Contains(runtimeImports.Resolve(RuntimeImportSymbol.RootFrameEnter), calls);
        Assert.Contains(runtimeImports.Resolve(RuntimeImportSymbol.RootFrameLeave), calls);
        Assert.Contains(30, calls);
    }

    [Fact]
    public void EmitsVoidCallbackWithoutReferenceParameters()
    {
        var method = new FakeProgram().GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.Void),
        };
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Callback", isValueType: false),
            [],
            method.Signature);
        var layouts = new RecordingLayoutProvider();
        var terminalBoundary = new PassthroughTerminalBoundary();
        var runtimeImports = WasmRuntimeImports.CreateCatalog();
        var runtimeImportSelection = new RuntimeImportSelection(
            WasmModuleProfile.ComponentCoreModule,
            IncludeTerminalExceptionReporter: true);
        IHostCallbackFunctionEmitter emitter = new[]
        {
            new HostCallbackFunctionEmitter(
                layouts,
                runtimeImports,
                new RecordingRuntimeInitializer(),
                new ImplicitExceptionEmitter(layouts, layouts),
                new JavaScriptReferenceArgumentEmitter(
                    new RecordingStringMarshaller(), new RecordingByteArrayMarshaller(), new RecordingHostObjectMarshaller()),
                EmitterTestSupport.ValueFramePlanner(layouts),
                EmitterTestSupport.ValueFrames(layouts, runtimeImports),
                terminalBoundary,
                new AddressInstructionEmitter(layouts),
                new GeneratedFunctionWriterFactory()),
        }.Cast<IHostCallbackFunctionEmitter>().Single();
        var indices = new FunctionIndexMap(
            [],
            [],
            ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
                instance.DeclaringType.CanonicalName,
                new WasmFunctionIndex(30)),
            [])
        {
            ManagedThrowFunctionIndex = OptionalFunctionIndex.At(EmitterTestSupport.ManagedThrowFunction),
        };

        Assert.Single(CreateFunctionTypes().Resolve(instance).Parameters);

        var body = emitter.Emit(
            new HostCallbackDeclaration(EntryKey, 0, instance, "callback"),
            indices,
            TestRuntimeInitialization.Create(256),
            new InteropImportPlan(
                [],
                OptionalFunctionIndex.Missing,
                OptionalFunctionIndex.Missing,
                OptionalFunctionIndex.Missing,
                OptionalFunctionIndex.Missing,
                OptionalFunctionIndex.Missing,
                OptionalFunctionIndex.Missing),
            runtimeImportSelection);

        Assert.Contains(WasmOpcodes.Call, body);
        Assert.DoesNotContain(WasmOpcodes.LocalSet, body[^2..]);
        Assert.Equal(runtimeImports.Resolve(
            RuntimeImportSymbol.ManagedTerminalExceptionReport,
            runtimeImportSelection), terminalBoundary.ReportFunctionIndex);
    }

    private static IHostCallbackFunctionTypeResolver CreateFunctionTypes() =>
        new[] { new HostCallbackFunctionTypeResolver() }
            .Cast<IHostCallbackFunctionTypeResolver>()
            .Single();

    private sealed class RecordingRuntimeInitializer : IRuntimeStateInitializer
    {
        public bool Called { get; private set; }

        public void Initialize(
            GeneratedFunctionWriterLease code,
            RuntimeInitializationPlan initialization) =>
            Called = true;
    }

    private sealed class RecordingStringMarshaller :
        IHostCallbackStringArgumentMarshaller
    {
        public int Calls { get; private set; }

        public bool Called => Calls != 0;

        public void Emit(
            IWasmInstructionWriter code,
            int handleParameter,
            int destination,
            int temporaryI4,
            int objectTemporary,
            InteropMarshallingTarget target,
            OptionalFunctionIndex throwFunction) => Calls++;
    }

    private sealed class RecordingHostObjectMarshaller :
        IHostCallbackHostObjectArgumentMarshaller
    {
        public bool Called { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            CliTypeIdentity objectType,
            int handleParameter,
            int destination,
            OptionalFunctionIndex throwFunction) => Called = true;
    }

    private sealed class RecordingByteArrayMarshaller :
        IHostCallbackByteArrayArgumentMarshaller
    {
        public bool Called { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            CliTypeIdentity arrayType,
            int handleParameter,
            int destination,
            int temporaryI4,
            int objectTemporary,
            InteropMarshallingTarget target,
            OptionalFunctionIndex throwFunction) => Called = true;
    }

    private sealed class PassthroughTerminalBoundary :
        IManagedTerminalExceptionBoundaryEmitter
    {
        public int? ReportFunctionIndex { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            int exceptionLocal,
            int rootFrameLocal,
            int typeIdLocal,
            int messageLocal,
            int messageLengthLocal,
            int stackTraceLocal,
            int stackTraceLengthLocal,
            CliValueKind resultType,
            int resultLocal,
            int reportFunctionIndex,
            int? raiseFunctionIndex,
            Action emitBody)
        {
            ReportFunctionIndex = reportFunctionIndex;
            emitBody();
        }
    }
}
