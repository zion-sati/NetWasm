using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AsyncTaskCompletionEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CompletionObservesOnlyFaultsAndConsumesTheHandleBeforeReporting(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var writers = new RecordingWriters();
        var state = new RecordingExceptionState();
        var emitter = Assert.IsAssignableFrom<IAsyncTaskCompletionEmitter>(new AsyncTaskCompletionEmitter(
            layouts, layouts, imports, state, writers));
        var binding = Binding();
        var plan = new AsyncTaskCompletionPlan(900, 901);

        emitter.Emit(binding, plan);

        var instructions = writers.Instructions.ToInstructions().ToList();
        // Captured from the export completion when the process completion it shared
        // the emitter with was removed; the export path's instructions did not
        // change. All encoded instructions, including operands and exception
        // handlers, must remain unchanged.
        var snapshot = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(instructions);
        Assert.Equal(target == WasmTarget.Wasm32
                ? "df632d19079505660b88dd62d066a95b4731a22484e9883e5b3006439245121b"
                : "6990bfa135aa36cd927c766fa06876f51d943e3d72a013d8500159f50254831f",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(snapshot)));
        int CallIndex(int index) => instructions.FindIndex(i =>
            i.Opcode == WasmOpcodes.Call && i.Operand.UnsignedValue == index);
        var handleGet = CallIndex(imports.Resolve(RuntimeImportSymbol.HandleGet));
        var observe = CallIndex(plan.ObserveFunctionIndex);
        var root = CallIndex(imports.Resolve(RuntimeImportSymbol.RootFrameEnter));
        var releaseHandle = CallIndex(imports.Resolve(RuntimeImportSymbol.HandleRelease));
        var endCatch = CallIndex(imports.Resolve(RuntimeImportSymbol.EndCatch));
        var report = CallIndex(plan.CaptureFunctionIndex);
        Assert.Equal(WasmOpcodes.LocalGet, instructions[handleGet - 1].Opcode);
        Assert.Equal(WasmOpcodes.LocalTee, instructions[handleGet + 1].Opcode);
        Assert.Equal(target == WasmTarget.Wasm64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero,
            instructions[handleGet + 2].Opcode);
        // A missing handle completes with no payload.
        Assert.Equal(WasmOpcodes.I32Constant, instructions[handleGet + 4].Opcode);
        Assert.Equal(WasmOpcodes.Return, instructions[handleGet + 5].Opcode);
        var statusLoad = instructions.FindIndex(i => i.Opcode == WasmOpcodes.I32Load);
        Assert.Equal((uint)layouts.GetFieldLayout(binding.StatusField).Offset, instructions[statusLoad].Operand.Offset);
        Assert.Equal(2, instructions[statusLoad + 1].Operand.SignedValue);
        Assert.Equal(WasmOpcodes.I32Equal, instructions[statusLoad + 2].Opcode);
        Assert.Equal(WasmOpcodes.If, instructions[statusLoad + 3].Opcode);
        Assert.True(statusLoad < observe && observe < root && root < releaseHandle && releaseHandle < report);
        Assert.True(root < endCatch && endCatch < releaseHandle);
        // Observation runs inside an exception frame whose landing pad rewinds the
        // frames the fault left before the catch allocates its own root frame.
        var enter = CallIndex(imports.Resolve(RuntimeImportSymbol.ExceptionFrameEnter));
        var unwind = CallIndex(imports.Resolve(RuntimeImportSymbol.ExceptionFrameUnwind));
        Assert.True(statusLoad < enter && enter < observe && observe < unwind && unwind < root);
        Assert.Equal(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned(4)),
            instructions[enter + 1]);
        Assert.Equal(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(4)),
            instructions[unwind - 1]);
        Assert.Equal((2, 4, 5, 6, 7, 8), state.Locals);
        Assert.Same(writers.Instructions, state.Writer);
        var catches = instructions.Where(i => i.Opcode == WasmOpcodes.TryTable).ToArray();
        Assert.Equal(2, catches.Length);
        Assert.Equal(0, catches[0].Operand.ByteValue);
        Assert.Equal(0u, catches[0].Operand.TagIndex);
        Assert.Equal(3, catches[1].Operand.ByteValue);
        Assert.Equal(0u, catches[1].Operand.LabelDepth);
        var leaveRoot = instructions.Select((instruction, index) => (instruction, index))
            .Where(item => item.instruction.Opcode == WasmOpcodes.Call &&
                item.instruction.Operand.UnsignedValue == imports.Resolve(RuntimeImportSymbol.RootFrameLeave))
            .Select(item => item.index).ToArray();
        Assert.Equal(2, leaveRoot.Length);
        Assert.True(leaveRoot.All(index => index > report));
        Assert.Equal(WasmOpcodes.Branch, instructions[leaveRoot[0] + 1].Opcode);
        Assert.Equal(WasmOpcodes.ThrowRef, instructions[leaveRoot[1] + 1].Opcode);
        Assert.DoesNotContain(instructions, i => i.Opcode == WasmOpcodes.Call &&
            i.Operand.UnsignedValue == imports.Resolve(RuntimeImportSymbol.ManagedTerminalExceptionRaise));
        Assert.Equal(2, instructions.Count(i => i.Opcode == WasmOpcodes.Call &&
            i.Operand.UnsignedValue == imports.Resolve(RuntimeImportSymbol.HandleRelease)));
    }

    [Fact]
    public void InvalidPlansFailBeforeCreatingAnOutput()
    {
        var layouts = new RecordingLayoutProvider();
        var writers = new RecordingWriters();
        var emitter = Assert.IsAssignableFrom<IAsyncTaskCompletionEmitter>(new AsyncTaskCompletionEmitter(
            layouts, layouts, WasmRuntimeImports.CreateCatalog(), new RecordingExceptionState(), writers));
        var binding = Binding();
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, new(0, 0)));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(binding, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => emitter.Emit(binding, new(-1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => emitter.Emit(binding, new(0, -1)));
        Assert.Equal(0, writers.Calls);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ExportCompletionReturnsOwnedPayloadAfterRootCleanupAndZeroForMissingHandles(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var imports = WasmRuntimeImports.CreateCatalog();
        var writers = new RecordingWriters();
        var emitter = new AsyncTaskCompletionEmitter(layouts, layouts, imports, new RecordingExceptionState(), writers);
        emitter.Emit(Binding(), new(900, 901));
        var instructions = writers.Instructions.ToInstructions().ToList();
        var missingReturn = instructions.FindIndex(i => i.Opcode == WasmOpcodes.Return);
        Assert.Equal(WasmOpcodes.I32Constant, instructions[missingReturn - 1].Opcode);
        Assert.Equal(0, instructions[missingReturn - 1].Operand.SignedValue);
        var capture = instructions.FindIndex(i => i.Opcode == WasmOpcodes.Call && i.Operand.UnsignedValue == 901);
        Assert.Equal(WasmOpcodes.LocalSet, instructions[capture + 1].Opcode);
        Assert.Equal(9u, instructions[capture + 1].Operand.UnsignedValue);
        var cleanup = instructions.FindIndex(capture, i => i.Opcode == WasmOpcodes.Call
            && i.Operand.UnsignedValue == imports.Resolve(RuntimeImportSymbol.RootFrameLeave));
        Assert.True(cleanup > capture);
        Assert.Equal(WasmOpcodes.LocalGet, instructions[^2].Opcode);
        Assert.Equal(9u, instructions[^2].Operand.UnsignedValue);
        Assert.Equal(2, instructions.Count(i => i.Opcode == WasmOpcodes.Call
            && i.Operand.UnsignedValue == imports.Resolve(RuntimeImportSymbol.RootFrameLeave)));
        Assert.Equal(2, instructions.Count(i => i.Opcode == WasmOpcodes.Call
            && i.Operand.UnsignedValue == imports.Resolve(RuntimeImportSymbol.HandleRelease)));
        Assert.Contains(instructions, i => i.Opcode == WasmOpcodes.ThrowRef);
    }

    private static JavaScriptAsyncMethodBinding Binding()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var taskType = CliTypeIdentity.Named(Assembly, "Test", "Task", false);
        var instance = new MethodInstanceModel(method, taskType, [], method.Signature);
        return new(EntryKey, new(JavaScriptAsyncReturnKind.Task, null), taskType, instance, instance, instance)
        {
            StatusField = new(program.GetField(InstanceFieldKey), taskType, CliTypeIdentity.FromStackKind(CliValueKind.I4)),
        };
    }

    private sealed class RecordingExceptionState : IExceptionObjectStateReader
    {
        public IWasmInstructionWriter? Writer { get; private set; }
        public (int, int, int, int, int, int) Locals { get; private set; }
        public void Emit(IWasmInstructionWriter code, int exceptionLocal, int typeIdLocal,
            int messageLocal, int messageLengthLocal, int stackTraceLocal, int stackTraceLengthLocal)
        {
            Writer = code;
            Locals = (exceptionLocal, typeIdLocal, messageLocal, messageLengthLocal, stackTraceLocal, stackTraceLengthLocal);
        }
    }

    private sealed class RecordingWriters : IGeneratedFunctionWriterFactory
    {
        public RecordingInstructionWriter Instructions { get; } = new();
        public int Calls { get; private set; }
        public GeneratedFunctionWriterLease Create()
        {
            Calls++;
            var buffer = new WasmBinaryBuffer();
            return new(new WasmBinaryWriter(buffer), new WasmBinarySnapshotReader(buffer), Instructions);
        }
    }
}
