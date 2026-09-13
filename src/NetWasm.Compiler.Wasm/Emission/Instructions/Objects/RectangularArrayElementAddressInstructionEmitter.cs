using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class RectangularArrayElementAddressInstructionEmitter(
    ITargetLayout layouts,
    IRectangularArrayElementAddressEmitter addresses) : InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        new(
            CilOperation.LoadRectangularArrayElementAddress,
            InstructionFamily.ArraysFieldsStatics,
            Emit),
    ];

    private void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        var arrayType = ((CilOperand.TypeIdentity)request.Instruction.Operand).Value;
        var arraySlot = request.Stack.Count - arrayType.ArrayRank - 1;
        addresses.Emit(new(request, arraySlot), code);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)GetStackLocal(
                request.Context,
                arraySlot,
                CliValueKind.ManagedAddress))));
        request.Stack.RemoveRange(arraySlot + 1, arrayType.ArrayRank);
        request.Stack[arraySlot] = CliValueKind.ManagedAddress;
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
