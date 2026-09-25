using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class StaticInitializationFailureEmitter(
    ITargetLayout layouts,
    IRuntimeImportResolver imports,
    IGeneratedFunctionWriterFactory writers) : IStaticInitializationFailureEmitter
{
    public byte[] Emit(StaticInitializerFunctionPlan plan)
    {
        var output = writers.Create();
        var code = output.Instructions;
        const int guard = 0;
        const int original = 1;
        const int handle = 2;
        WasmLocalDeclarationWriter.Write(output.Bytes, [CliValueKind.I4], layouts.Target);
        Local(WasmOpcodes.LocalGet, guard);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, 0)));
        Constant(1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        Block(WasmOpcodes.If);
        Local(WasmOpcodes.LocalGet, original);
        Call(RuntimeImportSymbol.HandleNew);
        Local(WasmOpcodes.LocalTee, handle);
        // Reserve 0, 1 and 2. A nonzero handle must fit after adding two.
        Constant(1);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Subtract));
        Constant(unchecked((int)0xfffffffc));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32GreaterThanUnsigned));
        Block(WasmOpcodes.If);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
        End();
        Local(WasmOpcodes.LocalGet, guard);
        Local(WasmOpcodes.LocalGet, handle);
        Constant(2);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Add));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store, WasmInstructionOperand.Memory(2, 0)));
        // Publish the permanent root before ending the catch or searching outer
        // filters. Filter reentry must observe the same failed initialization.
        Call(RuntimeImportSymbol.EndCatch);
        End();

        Local(WasmOpcodes.LocalGet, original);
        Call(RuntimeImportSymbol.BeginThrow);
        Local(WasmOpcodes.LocalGet, original);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Throw, WasmInstructionOperand.Unsigned(0)));
        End();
        return output.Snapshots.Read();

        void Constant(int value) => code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(value)));
        void Local(byte opcode, int index) => code.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Unsigned((uint)index)));
        void Block(byte opcode) => code.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        void Call(RuntimeImportSymbol symbol) => code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)imports.Resolve(symbol))));
        void End() => code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
