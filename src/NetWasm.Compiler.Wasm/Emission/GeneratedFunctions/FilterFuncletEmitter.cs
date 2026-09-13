using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class FilterFuncletEmitter(
ITargetLayout layouts,
    IExceptionPayloadBlockEmitter exceptions,
    IGeneratedFunctionWriterFactory writers) : IFilterFuncletEmitter
{
    public FilterFuncletEmission Emit(
        FilterFunclet filter,
        FilterEnvironmentLayout environment,
        IFilterSequenceEmitter emitSequence)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(emitSequence);

        var header = filter.Method.Header;
        const int parameterCount = 2;
        var localBase = parameterCount;
        var stackBase = checked(localBase + header.Locals.Length);
        var stackLocals = WasmLocalLayoutPlanner.CreateEvaluationStack(
            stackBase,
            header.MaxStack);
        var objectTemporary = stackLocals.End;
        var rootFrame = checked(objectTemporary + 1);
        var exceptionTemporary = checked(rootFrame + 1);
        var filterRootFrame = checked(exceptionTemporary + 1);
        var interopDescriptor = checked(filterRootFrame + 1);
        var interopResult = checked(interopDescriptor + 1);
        var interopHandle = checked(interopResult + 1);
        var numericTemporaryI4 = checked(interopHandle + 1);
        var numericTemporaryI8 = checked(numericTemporaryI4 + 1);
        var emptyRootMap = new MethodRootMap(
            header.Method.Key,
            [],
            []);
        var context = new MethodEmissionContext(
            emptyRootMap,
            localBase,
            stackBase,
            stackLocals,
            objectTemporary,
            rootFrame,
            exceptionTemporary,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            ImmutableDictionary<StructuredExceptionGroupId, int>.Empty,
            1,
            new ValueFrameLayout(
                environment.Size,
                [],
                [],
                [],
                []),
            filterRootFrame,
            environment,
            0,
            interopDescriptor,
            interopResult,
            interopHandle,
            numericTemporaryI4,
            numericTemporaryI8,
            numericTemporaryI4,
            LeaveFrameOnRethrow: false,
            LeaveFrameOnExceptionalExit: false,
            IsFilterFunclet: true,
            CallerIdentity: filter.CallerIdentity);
        var output = writers.Create();
        var code = output.Instructions;
        WasmLocalDeclarationWriter.Write(
            output.Bytes,
            WasmLocalLayoutPlanner.CreateFilterLocals(header),
            layouts.Target);
        WriteLocalGet(code, 1);
        if (environment.RootFrameOffset != 0)
        {
            EmitAddressConstant(code, environment.RootFrameOffset);
            EmitAddressAdd(code);
        }
        EmitReferenceLoad(code);
        WriteLocalSet(code, filterRootFrame);
        exceptions.Emit(code);
        WriteTryTableCatch(code, 0, 0);
        WriteLocalGet(code, 0);
        WriteLocalSet(code, WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            stackLocals,
            0,
            CliValueKind.ManagedReference,
            layouts.Target));
        var sequenceEmission = emitSequence.Emit(
            code,
            filter.Method,
            filter.Clause.FilterBody ?? throw new InvalidOperationException(
                "A filter clause has no filter body."),
            context);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Drop));
        WriteI32Constant(code, 0);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return new(
            output.Snapshots.Read(),
            sequenceEmission.OriginalBlockEmissionCounts);
    }

    private void EmitAddressConstant(IWasmInstructionWriter code, int value)
    {
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            WriteI32Constant(code, value);
        }
    }

    private void EmitAddressAdd(IWasmInstructionWriter code)
    {
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I64Add));
        }
        else
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        }
    }

    private void EmitReferenceLoad(IWasmInstructionWriter code)
    {
        var (opcode, alignment) = layouts.Target.UsesMemory64
            ? (WasmOpcodes.I64Load, 3u)
            : (WasmOpcodes.I32Load, 2u);
        code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.Memory(alignment, 0)));
    }

    private static void WriteLocalGet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteLocalSet(IWasmInstructionWriter code, int index) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(checked((uint)index))));

    private static void WriteI32Constant(IWasmInstructionWriter code, int value) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));

    private static void WriteTryTableCatch(
        IWasmInstructionWriter code,
        int tagIndex,
        int labelDepth) =>
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(
                WasmOpcodes.EmptyBlockType,
                checked((uint)tagIndex),
                checked((uint)labelDepth))));
}
