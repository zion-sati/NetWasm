using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class FilterDispatcherEmitter(
    IGeneratedFunctionWriterFactory writers) : IFilterDispatcherEmitter
{
    public byte[] Emit(
        ImmutableArray<FilterFunclet> filters,
        IReadOnlyDictionary<int, int> functionIndices)
    {
        var code = writers.Create();
        code.Bytes.Write([0]);
        foreach (var filter in filters.OrderBy(filter => filter.Id))
        {
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned(0)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(filter.Id)));
            code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
            code.Instructions.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned(1)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned(2)));
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)functionIndices[filter.Id])));
            code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Return));
            code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }
}
