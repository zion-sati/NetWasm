using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Array.CreateInstance for rank-N arrays: selects a constructed rectangular array type by
// element type and rank, then allocates it with the plain or lower-bounded allocator.
// The bounds address holds the lengths followed by the lower bounds. Unknown types leave
// the result null for the caller's NotSupportedException.
internal sealed class ArrayCreateRectangularInstanceIntrinsicEmitter(
    ITypeDescriptorSource descriptors,
    ITypeLayoutProvider types,
    IValueLayoutProvider values,
    IRuntimeImportResolver imports,
    IRootPublicationEmitter roots,
    IAddressInstructionEmitter addresses,
    IImplicitExceptionEmitter exceptions) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var elementId = request.Instruction.Context.NumericTemporaryI4;
        var rank = request.Instruction.Context.NumericTemporaryI4Second;
        var result = request.Local(0, CliValueKind.ManagedReference);
        Get(code, request.Local(0, CliValueKind.I4));
        Set(code, elementId);
        Get(code, request.Local(1, CliValueKind.I4));
        Set(code, rank);
        roots.Emit(request.Instruction, code);
        addresses.Emit(code, 0);
        Set(code, result);
        foreach (var descriptor in descriptors.ConstructedTypeDescriptors)
        {
            if (descriptor.Type.Shape != CliTypeShape.Array || descriptor.Type.ContainsGenericParameters)
                continue;
            var element = descriptor.Type.ElementType!;
            var typeId = types.GetObjectLayout(element).TypeId;
            Get(code, elementId);
            I32(code, typeId);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            Get(code, rank);
            I32(code, descriptor.Type.ArrayRank);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32And));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            Get(code, request.Local(3, CliValueKind.I4));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            Allocate(request, code, descriptor, element, typeId, rank, RuntimeImportSymbol.AllocateBoundedRectangularArray);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
            Allocate(request, code, descriptor, element, typeId, rank, RuntimeImportSymbol.AllocateRectangularArray);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            Get(code, result);
            addresses.Emit(code, AddressOperation.EqualZero);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.If,
                WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            exceptions.Emit(code, ManagedExceptionKind.OutOfMemory, request.Instruction.Context.ManagedThrowFunction);
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }

    private void Allocate(
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code,
        ConstructedTypeDescriptorLayout descriptor,
        CliTypeIdentity element,
        int elementTypeId,
        int rank,
        RuntimeImportSymbol import)
    {
        var elementsAreReferences = element.StackKind == CliValueKind.ManagedReference;
        Get(code, rank);
        Get(code, request.Local(2, CliValueKind.NativeInt));
        I32(code, descriptor.TypeId);
        I32(code, elementTypeId);
        I32(code, elementsAreReferences ? 0 : values.GetValueLayout(element).Size);
        I32(code, elementsAreReferences ? 1 : 0);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)imports.Resolve(import,
                request.Instruction.Target.RuntimeImportSelection))));
        Set(code, request.Local(0, CliValueKind.ManagedReference));
    }

    private static void Get(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void Set(IWasmInstructionWriter code, int local) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)local)));
    private static void I32(IWasmInstructionWriter code, int value) => code.Write(
        WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
}
