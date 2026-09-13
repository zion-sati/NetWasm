using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class FinalizerDispatcherEmitter(
    ITargetLayout layouts,
    IExceptionPayloadBlockEmitter exceptions,
    IGeneratedFunctionWriterFactory writers) : IFinalizerDispatcherEmitter
{
    public byte[] Emit(
        TypeDescriptorLayout[] finalizableTypes,
        IFunctionIndexResolver functionIndices)
    {
        if (finalizableTypes.Length == 0)
        {
            var empty = writers.Create();
            empty.Bytes.Write([0]);
            empty.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(2)));
            empty.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            return empty.Snapshots.Read();
        }

        const int objectParameter = 0;
        const int typeIdParameter = 1;
        const int statusLocal = 2;
        const int exceptionLocal = 3;
        var code = writers.Create();
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            [CliValueKind.I4, CliValueKind.ManagedReference],
            layouts.Target);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(2)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)statusLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.Block, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code.Instructions);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.TryTable,
            WasmInstructionOperand.TryTableCatch(
                WasmOpcodes.EmptyBlockType,
                0,
                0)));
        foreach (var descriptor in finalizableTypes)
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)typeIdParameter)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(descriptor.TypeId)));
            code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)objectParameter)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices.Resolve(
                    descriptor.Finalizer!.Value))));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(0)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalSet,
                WasmInstructionOperand.Unsigned((uint)statusLocal)));
            code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Branch,
            WasmInstructionOperand.Unsigned(1)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)exceptionLocal)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(1)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)statusLocal)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)statusLocal)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
