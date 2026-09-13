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
                new ImplicitExceptionEmitter(layouts, layouts, 7),
                stringMarshaller,
                byteMarshaller,
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
            []);

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
                new ImplicitExceptionEmitter(layouts, layouts, 7),
                new RecordingStringMarshaller(),
                new RecordingByteArrayMarshaller(),
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
            []);

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
        public bool Called { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            int handleParameter,
            int destination,
            int temporaryI4,
            int objectTemporary,
            InteropMarshallingTarget target) => Called = true;
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
            InteropMarshallingTarget target) => Called = true;
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
            CliValueKind resultType,
            int resultLocal,
            int reportFunctionIndex,
            Action emitBody)
        {
            ReportFunctionIndex = reportFunctionIndex;
            emitBody();
        }
    }
}
