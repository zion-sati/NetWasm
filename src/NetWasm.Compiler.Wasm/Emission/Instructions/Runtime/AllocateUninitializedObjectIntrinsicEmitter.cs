using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// RuntimeHelpers.GetUninitializedObject: allocates zeroed storage for the admitted type whose
// semantic type ID matches, exactly as newobj and box allocate before any constructor or copy.
// Other types leave the result null for the caller's PlatformNotSupportedException.
internal sealed class AllocateUninitializedObjectIntrinsicEmitter(
    IUninitializedObjectSource types,
    ITypeLayoutProvider layouts,
    IRuntimeImportResolver imports,
    IRootPublicationEmitter roots,
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var typeId = request.Instruction.Context.NumericTemporaryI4;
        var result = request.Local(0, CliValueKind.ManagedReference);
        Get(code, request.Local(0, CliValueKind.I4));
        Set(code, typeId);
        roots.Emit(request.Instruction, code);
        addresses.Emit(code, 0);
        Set(code, result);
        foreach (var type in types.UninitializedObjectTypes)
        {
            var layout = layouts.GetObjectLayout(type);
            var allocation = request.Instruction.Target.FinalizableTypeIds.Contains(layout.TypeId)
                ? RuntimeImportSymbol.AllocateFinalizable
                : RuntimeImportSymbol.Allocate;
            Get(code, typeId);
            I32(code, layout.TypeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            addresses.Emit(code, layout.Size);
            I32(code, layout.TypeId);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)imports.Resolve(allocation,
                    request.Instruction.Target.RuntimeImportSelection))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalTee,
                WasmInstructionOperand.Unsigned((uint)result)));
            addresses.Emit(code, AddressOperation.EqualZero);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, request.Instruction.Context.ManagedThrowFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void I32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
