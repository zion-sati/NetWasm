// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class StringEqualsOrdinalIntrinsicEmitter(
    IRuntimeObjectLayout objects,
    IAddressInstructionEmitter addresses) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var left = request.Local(0, CliValueKind.ManagedReference);
        var right = request.Local(1, CliValueKind.ManagedReference);
        var remaining = request.Instruction.Context.NumericTemporaryI4;
        var result = request.Local(0, CliValueKind.I4);

        Get(left);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)objects.StringLengthOffset)));
        Set(remaining);

        Advance(left, objects.StringDataOffset);
        Advance(right, objects.StringDataOffset);

        Block(WasmOpcodes.Block);
        Block(WasmOpcodes.Block);
        Block(WasmOpcodes.Loop);

        Get(remaining);
        Op(WasmOpcodes.I32EqualZero);
        Branch(WasmOpcodes.BranchIf, 1);

        Get(left);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load16Unsigned,
            WasmInstructionOperand.Memory(1, 0)));
        Get(right);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load16Unsigned,
            WasmInstructionOperand.Memory(1, 0)));
        Op(WasmOpcodes.I32Equal);
        Block(WasmOpcodes.If);

        Advance(left, sizeof(char));
        Advance(right, sizeof(char));
        Get(remaining);
        Integer(1);
        Op(WasmOpcodes.I32Subtract);
        Set(remaining);
        Branch(WasmOpcodes.Branch, 1);

        Op(WasmOpcodes.End);
        Integer(0);
        Set(result);
        Branch(WasmOpcodes.Branch, 2);

        Op(WasmOpcodes.End);
        Op(WasmOpcodes.End);
        Integer(1);
        Set(result);
        Op(WasmOpcodes.End);

        void Advance(int local, int offset)
        {
            Get(local);
            addresses.Emit(code, offset);
            addresses.Emit(code, AddressOperation.Add);
            Set(local);
        }

        void Get(int local) => code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)local)));
        void Set(int local) => code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)local)));
        void Branch(byte opcode, uint depth) => code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.Unsigned(depth)));
        void Block(byte opcode) => code.Write(WasmInstruction.WithOperand(
            opcode,
            WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        void Integer(int value) => code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(value)));
        void Op(byte opcode) => code.Write(WasmInstruction.NoOperand(opcode));
    }
}
