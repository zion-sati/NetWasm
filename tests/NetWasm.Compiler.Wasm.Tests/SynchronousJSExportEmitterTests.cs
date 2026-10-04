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

public sealed class SynchronousJSExportEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32EqualZero, false, false)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64EqualZero, false, false)]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32EqualZero, true, false)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64EqualZero, true, true)]
    public void ScalarVoidExportRunsFinalizersWithoutRooting(
        WasmTarget target,
        byte expectedEqualZero,
        bool includeReporter,
        bool includeRaise)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey) with
        {
            Signature = MethodSignatureModel.Create(
                CliValueKind.Void,
                CliValueKind.I4),
        };
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey,
                new(30)),
            [],
            [],
            []);
        var runtime = new RecordingRuntimeInitializer();
        var strings = new RecordingStringMarshaller();
        var byteArrays = new RecordingByteArrayMarshaller();
        var terminal = new RecordingTerminalBoundary();
        var emitter = new SynchronousJSExportEmitter(
            layouts,
            imports,
            runtime,
            strings,
            byteArrays,
            terminal,
            terminal,
            new AddressInstructionEmitter(layouts),
            new GeneratedFunctionWriterFactory());
        var selection = new RuntimeImportSelection(
            WasmModuleProfile.CoreApplication,
            IncludeTerminalExceptionReporter: includeReporter,
            IncludeTerminalExceptionRaise: includeRaise);
        var initialization = TestRuntimeInitialization.Create(512, selection);

        var body = emitter.Emit(
            method,
            initialization,
            hasFinalizers: true,
            new FunctionIndexResolver(program, program, indices),
            EmptyInteropImports());

        Assert.True(runtime.Called);
        Assert.False(strings.Called);
        Assert.False(byteArrays.Called);
        Assert.Null(terminal.CatchCleanup);
        Assert.Equal(includeReporter, terminal.ReportCalled);
        Assert.Equal(!includeReporter, terminal.TrapCalled);
        Assert.Equal(includeRaise
            ? imports.Resolve(RuntimeImportSymbol.ManagedTerminalExceptionRaise, selection)
            : (int?)null, terminal.RaiseFunctionIndex);
        if (includeReporter)
            Assert.Equal(imports.Resolve(RuntimeImportSymbol.ManagedTerminalExceptionReport, selection),
                terminal.ReportFunctionIndex);
        Assert.True(body.AsSpan().IndexOf(new byte[]
        {
            WasmOpcodes.LocalGet,
            0,
            WasmOpcodes.Call,
            30,
        }) >= 0);
        Assert.True(body.AsSpan().IndexOf(new byte[]
        {
            WasmOpcodes.Call,
            (byte)imports.Resolve(
                RuntimeImportSymbol.FinalizerSafepoint,
                initialization.RuntimeImportSelection),
        }) >= 0);
        Assert.Contains(expectedEqualZero, body);
        Assert.Contains(WasmOpcodes.Throw, body);
        Assert.DoesNotContain(
            (byte)imports.Resolve(RuntimeImportSymbol.RootFrameEnter),
            Calls(body));
        Assert.DoesNotContain(
            (byte)imports.Resolve(RuntimeImportSymbol.RootFrameLeave),
            Calls(body));
    }

    private static InteropImportPlan EmptyInteropImports() => new(
        [],
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing,
        OptionalFunctionIndex.Missing);

    private static byte[] Calls(byte[] body) => [.. body
        .Select((value, index) => (value, index))
        .Where(item =>
            item.value == WasmOpcodes.Call &&
            item.index + 1 < body.Length)
        .Select(item => body[item.index + 1])];

    private sealed class RecordingRuntimeInitializer : IRuntimeStateInitializer
    {
        public bool Called { get; private set; }

        public void Initialize(
            GeneratedFunctionWriterLease code,
            RuntimeInitializationPlan initialization) => Called = true;
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

    private sealed class RecordingTerminalBoundary :
        IManagedTerminalExceptionBoundaryEmitter,
        IManagedTerminalTrapBoundaryEmitter
    {
        public Action? CatchCleanup { get; private set; }
        public bool ReportCalled { get; private set; }
        public bool TrapCalled { get; private set; }
        public int? ReportFunctionIndex { get; private set; }
        public int? RaiseFunctionIndex { get; private set; }

        public void Emit(
            IWasmInstructionWriter code,
            CliValueKind resultType,
            int resultLocal,
            Action emitBody)
        {
            TrapCalled = true;
            emitBody();
        }

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
            ReportCalled = true;
            ReportFunctionIndex = reportFunctionIndex;
            RaiseFunctionIndex = raiseFunctionIndex;
            CatchCleanup = emitCatchCleanup;
            emitBody();
        }
    }
}
