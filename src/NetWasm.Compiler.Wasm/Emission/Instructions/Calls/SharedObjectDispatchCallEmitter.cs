using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

// A virtual call on object with many targets calls CoreLib's shared body for it, which carries the one copy of the
// dispatch (CallEmissionKindResolver chooses this emitter); the shared body's own call is the one that dispatches.
internal sealed class SharedObjectDispatchCallEmitter(
    ITargetLayout layouts,
    IImplicitExceptionEmitter exceptions,
    IConstrainedReferenceReceiverEmitter constrainedReceivers) : ICallEmitter
{
    private readonly ITargetLayout _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly IImplicitExceptionEmitter _exceptions = exceptions ??
        throw new ArgumentNullException(nameof(exceptions));
    private readonly IConstrainedReferenceReceiverEmitter _constrainedReceivers = constrainedReceivers ??
        throw new ArgumentNullException(nameof(constrainedReceivers));

    public void Emit(CallEmissionRequest request, IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(functionIndices);
        var instruction = request.Instruction;
        var site = instruction.Target.DispatchCallSites.Required(
            $"{instruction.Header.MethodInstance!.CanonicalName}@{instruction.Instruction.Offset:x8}");
        var shared = instruction.Target.SharedObjectDispatch[site.Declaration.CanonicalName];
        var receiverLocal = _constrainedReceivers.Emit(instruction, code, request.ArgumentBase, request.ConstrainedType) ??
            StackLocal(instruction.Context, request.ArgumentBase, CliValueKind.ManagedReference);
        if (!ReceiverNullProof.Proves(instruction))
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)receiverLocal)));
            code.Write(WasmInstruction.NoOperand(_layouts.Target.UsesMemory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            _exceptions.Emit(code, ManagedExceptionKind.NullReference, instruction.Context.ManagedThrowFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)receiverLocal)));
        for (var index = 1; index < request.Consumed; index++)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)StackLocal(
                instruction.Context,
                request.ArgumentBase + index,
                instruction.Stack[request.ArgumentBase + index]))));
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(shared))));
        var returnType = request.Method.Signature.ReturnType;
        instruction.Stack.RemoveRange(request.ArgumentBase, request.Consumed);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)StackLocal(
            instruction.Context,
            request.ArgumentBase,
            returnType))));
        instruction.Stack.Add(returnType);
    }

    private int StackLocal(MethodEmissionContext context, int slot, CliValueKind type) =>
        WasmLocalLayoutPlanner.GetEvaluationStackLocal(context.StackLocals, slot, type, _layouts.Target);
}
