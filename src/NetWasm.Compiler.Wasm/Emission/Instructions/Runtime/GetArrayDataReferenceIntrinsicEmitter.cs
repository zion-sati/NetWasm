using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class GetArrayDataReferenceIntrinsicEmitter(
    ITargetLayout layouts,
    IRuntimeObjectLayout objects,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var arrayLocal = request.Local(0, CliValueKind.ManagedReference);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(arrayLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference, request.Instruction.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(arrayLocal))));
        // GetArrayDataReference<T>(T[]) takes a one-dimensional array, whose
        // elements are inline; the Array overload may take a rectangular one,
        // whose elements follow its shape, so it reads the data pointer every
        // array keeps.
        if (request.Method.Definition.GenericArity == 1)
        {
            ManagedMemoryEmitter.EmitArrayData(code, layouts.Target, request.Instruction.Context.InlineArrayData);
        }
        else
        {
            ManagedMemoryEmitter.EmitReferenceLoad(code, layouts.Target, objects.ArrayDataPointerOffset);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, CliValueKind.ManagedAddress)))));
    }
}
