using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class NativeCallbackThunkEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TrapsBeforeBoundaryThenInitializesAndCallsManagedBody(
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var initialization = new RecordingStaticInitializationEmitter();
        var terminal = new RecordingTerminalBoundaryEmitter();
        var writers = new RecordingWriterFactory();
        var callback = CallbackPlan();
        var moduleData = ModuleDataPlan.Empty with
        {
            NativeCallbackReadinessAddress = 256,
        };
        var selection = new RuntimeImportSelection(
            WasmModuleProfile.CoreApplication,
            IncludeTerminalExceptionReporter: true);
        var emitter = Assert.IsAssignableFrom<INativeCallbackThunkEmitter>(
            new NativeCallbackThunkEmitter(
                layouts,
                imports,
                new AddressInstructionEmitter(layouts),
                initialization,
                terminal,
                writers));

        emitter.Emit(
            callback,
            moduleData,
            selection,
            new FixedFunctionIndexResolver());

        var instructions = writers.Instructions.ToInstructions();
        Assert.Equal(6, terminal.EntryInstructionCount);
        Assert.Equal(
            target == WasmTarget.Wasm32
                ? WasmOpcodes.I32Constant
                : WasmOpcodes.I64Constant,
            instructions[0].Opcode);
        Assert.Equal(WasmOpcodes.I32Load, instructions[1].Opcode);
        Assert.Equal(WasmOpcodes.I32EqualZero, instructions[2].Opcode);
        Assert.Equal(WasmOpcodes.If, instructions[3].Opcode);
        Assert.Equal(WasmOpcodes.Unreachable, instructions[4].Opcode);
        Assert.Equal(WasmOpcodes.End, instructions[5].Opcode);
        Assert.Same(moduleData, initialization.Request!.ModuleData);
        Assert.True(initialization.Request.IsStaticMethodCall);
        Assert.Equal(callback.Method.DeclaringType, initialization.Request.DeclaringType);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == 44);
        Assert.Equal(
            imports.Resolve(
                RuntimeImportSymbol.ManagedTerminalExceptionReport,
                selection),
            terminal.ReportFunctionIndex);
        Assert.Equal(CliValueKind.I4, terminal.ResultType);
        Assert.Equal(WasmOpcodes.Return, instructions[^2].Opcode);
        Assert.Equal(WasmOpcodes.End, instructions[^1].Opcode);
    }

    [Fact]
    public void MissingReadinessPlanRejectsBeforeEnteringBoundary()
    {
        var layouts = new RecordingLayoutProvider();
        var terminal = new RecordingTerminalBoundaryEmitter();
        var emitter = Assert.IsAssignableFrom<INativeCallbackThunkEmitter>(
            new NativeCallbackThunkEmitter(
                layouts,
                WasmRuntimeImports.CreateCatalog(),
                new AddressInstructionEmitter(layouts),
                new RecordingStaticInitializationEmitter(),
                terminal,
                new RecordingWriterFactory()));

        Assert.Throws<InvalidOperationException>(() => emitter.Emit(
            CallbackPlan(),
            ModuleDataPlan.Empty,
            new(WasmModuleProfile.CoreApplication, true),
            new FixedFunctionIndexResolver()));
        Assert.False(terminal.Entered);
    }

    [Fact]
    public void VoidCallbackDoesNotAllocateAResultLocal()
    {
        var layouts = new RecordingLayoutProvider();
        var terminal = new RecordingTerminalBoundaryEmitter();
        var emitter = new NativeCallbackThunkEmitter(
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            new AddressInstructionEmitter(layouts),
            new RecordingStaticInitializationEmitter(),
            terminal,
            new RecordingWriterFactory());

        emitter.Emit(
            CallbackPlan(CliValueKind.Void),
            ModuleDataPlan.Empty with { NativeCallbackReadinessAddress = 256 },
            new(WasmModuleProfile.CoreApplication, true),
            new FixedFunctionIndexResolver());

        Assert.Equal(CliValueKind.Void, terminal.ResultType);
    }

    private static NativeCallbackMethodPlan CallbackPlan(
        CliValueKind returnType = CliValueKind.I4)
    {
        var program = new FakeProgram();
        var signature = MethodSignatureModel.Create(
            returnType,
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

    private sealed class RecordingWriterFactory : IGeneratedFunctionWriterFactory
    {
        public EmitterTestSupport.RecordingInstructionWriter Instructions { get; } = new();

        public GeneratedFunctionWriterLease Create()
        {
            var buffer = new WasmBinaryBuffer();
            return new(
                new WasmBinaryWriter(buffer),
                new WasmBinarySnapshotReader(buffer),
                Instructions);
        }
    }

    private sealed class RecordingStaticInitializationEmitter :
        IStaticInitializationEmitter
    {
        public StaticInitializationEmissionRequest? Request { get; private set; }

        public void Emit(
            StaticInitializationEmissionRequest request,
            IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices)
        {
            Request = request;
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned(91)));
        }
    }

    private sealed class RecordingTerminalBoundaryEmitter :
        IManagedTerminalExceptionBoundaryEmitter
    {
        public bool Entered { get; private set; }
        public int EntryInstructionCount { get; private set; }
        public int ReportFunctionIndex { get; private set; }
        public CliValueKind ResultType { get; private set; }

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
            Action emitBody,
            Action? emitCatchCleanup = null)
        {
            Entered = true;
            EntryInstructionCount = ((EmitterTestSupport.RecordingInstructionWriter)code)
                .ToInstructions().Length;
            ReportFunctionIndex = reportFunctionIndex;
            ResultType = resultType;
            emitBody();
        }
    }

    private sealed class FixedFunctionIndexResolver : IFunctionIndexResolver
    {
        public int Resolve(EntityKey method) => 44;
        public int Resolve(string method) => 44;
        public int Resolve(MethodInstanceModel method) => 44;
    }
}
