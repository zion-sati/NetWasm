using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncTaskCompletionEmitter(
    ITargetLayout layouts,
    IInstanceFieldLayoutProvider fields,
    IRuntimeImportResolver imports,
    IExceptionObjectStateReader exceptionState,
    IGeneratedFunctionWriterFactory writers) : IAsyncTaskCompletionEmitter
{
    public byte[] Emit(JavaScriptAsyncMethodBinding binding, AsyncTaskCompletionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentOutOfRangeException.ThrowIfNegative(plan.ObserveFunctionIndex);
        if (plan.DeliverFunctionIndex is { } deliverFunctionIndex)
            ArgumentOutOfRangeException.ThrowIfNegative(deliverFunctionIndex);
        if (plan.ResultKind is not (CliValueKind.Void or CliValueKind.I4))
            throw new ArgumentOutOfRangeException(nameof(plan));
        if (plan.DeliverFunctionIndex is null && plan.ResultKind != CliValueKind.Void)
            throw new ArgumentException(
                "A completion payload requires a delivery function.",
                nameof(plan));
        var returnsPayload = plan.ResultKind == CliValueKind.I4;
        var statusOffset = fields.GetFieldLayout(binding.StatusField).Offset;
        var code = writers.Create();
        const int task = 1, exception = 2, root = 3, type = 4;
        const int message = 5, messageLength = 6, trace = 7, traceLength = 8;
        const int payload = 9;
        ImmutableArray<CliValueKind> locals =
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference,
             CliValueKind.ManagedAddress, CliValueKind.I4, CliValueKind.ManagedReference,
             CliValueKind.I4, CliValueKind.ManagedReference, CliValueKind.I4];
        if (returnsPayload) locals = locals.Add(CliValueKind.I4);
        WasmLocalDeclarationWriter.Write(code.Bytes, locals, layouts.Target);

        LocalGet(0);
        Call(imports.Resolve(RuntimeImportSymbol.HandleGet));
        Local(WasmOpcodes.LocalTee, task);
        Op(layouts.Target.UsesMemory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero);
        Block(WasmOpcodes.If, WasmOpcodes.EmptyBlockType);
        if (returnsPayload)
            code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(0)));
        Op(WasmOpcodes.Return);
        Op(WasmOpcodes.End);
        LocalGet(task);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)statusOffset)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(2)));
        Op(WasmOpcodes.I32Equal);
        Block(WasmOpcodes.If, WasmOpcodes.EmptyBlockType);

        // Await semantics observe the fault tracker and preserve the original
        // exception/trace, including Task.WhenAll's first failure. The task handle
        // roots its exception until the catch establishes an explicit root.
        Block(WasmOpcodes.Block, (byte)(layouts.Target.UsesMemory64 ? WasmValueType.I64 : WasmValueType.I32));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(WasmOpcodes.EmptyBlockType, 0, 0)));
        LocalGet(task);
        Call(plan.ObserveFunctionIndex);
        Op(WasmOpcodes.Unreachable);
        Op(WasmOpcodes.End);
        Op(WasmOpcodes.Unreachable);
        Op(WasmOpcodes.End);
        Local(WasmOpcodes.LocalSet, exception);
        if (plan.DeliverFunctionIndex is null)
        {
            // Release the managed catch state and task handle before terminating.
            // Lean process builds deliberately carry no diagnostic payload ABI.
            Call(imports.Resolve(RuntimeImportSymbol.EndCatch));
            ReleaseHandle();
            Op(WasmOpcodes.Unreachable);
            Op(WasmOpcodes.Else);
            ReleaseHandle();
            Op(WasmOpcodes.End);
            Op(WasmOpcodes.End);
            return code.Snapshots.Read();
        }
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        Call(imports.Resolve(RuntimeImportSymbol.RootFrameEnter));
        Local(WasmOpcodes.LocalSet, root);
        ManagedMemoryEmitter.EmitRootSlotStore(code.Instructions, layouts.Target, root, 0, exception);
        // This catch returns to the host instead of terminating the instance.
        // Transfer ownership from the dispatch root to our explicit root.
        Call(imports.Resolve(RuntimeImportSymbol.EndCatch));
        ReleaseHandle();

        // Consume before reporting: a repeated/reentrant completion cannot report
        // twice. A foreign reporter exception must release our root and propagate
        // unchanged so the host preserves its cleanup-failure classification.
        Block(WasmOpcodes.Block, WasmOpcodes.EmptyBlockType);
        Block(WasmOpcodes.Block, (byte)WasmValueType.ExnRef);
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatchAllRef(WasmOpcodes.EmptyBlockType, 0)));
        exceptionState.Emit(code.Instructions, exception, type, message, messageLength, trace, traceLength);
        LocalGet(type);
        LocalGet(message);
        LocalGet(messageLength);
        LocalGet(trace);
        LocalGet(traceLength);
        Call(plan.DeliverFunctionIndex.Value);
        if (returnsPayload) Local(WasmOpcodes.LocalSet, payload);
        Op(WasmOpcodes.End);
        ReleaseRoot();
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        Op(WasmOpcodes.End);
        ReleaseRoot();
        Op(WasmOpcodes.ThrowRef);
        Op(WasmOpcodes.End);
        Op(WasmOpcodes.Else);
        ReleaseHandle();
        Op(WasmOpcodes.End);
        // The host owns only copied exception data. The managed root has been
        // released before returning; nonfault completion returns the zero local.
        if (returnsPayload) LocalGet(payload);
        Op(WasmOpcodes.End);
        return code.Snapshots.Read();

        void Op(byte opcode) => code.Instructions.Write(WasmInstruction.NoOperand(opcode));
        void LocalGet(int index) => Local(WasmOpcodes.LocalGet, index);
        void Local(byte opcode, int index) => code.Instructions.Write(WasmInstruction.WithOperand(
            opcode, WasmInstructionOperand.Unsigned((uint)index)));
        void Call(int index) => code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)index)));
        void Block(byte opcode, byte type) => code.Instructions.Write(WasmInstruction.WithOperand(
            opcode, WasmInstructionOperand.BlockType(type)));
        void ReleaseHandle()
        {
            LocalGet(0);
            Call(imports.Resolve(RuntimeImportSymbol.HandleRelease));
        }
        void ReleaseRoot()
        {
            LocalGet(root);
            Call(imports.Resolve(RuntimeImportSymbol.RootFrameLeave));
        }
    }
}
