namespace NetWasm.Compiler.Wasm.Encoding;

/// <summary>
/// Composition boundary for the standard WebAssembly operand encoders.
/// </summary>
public static class WasmInstructionEncoderRegistryFactory
{
    public static IWasmInstructionEncoderRegistry CreateDefault()
    {
        var unsigned = new UnsignedLeb128Encoder();
        var signed = new SignedLeb128Encoder();
        var signed64 = new SignedLeb12864Encoder();

        return new WasmInstructionEncoderRegistry(
        [
            new(WasmInstructionOperandShape.None, new NoOperandInstructionEncoder()),
            new(WasmInstructionOperandShape.BlockType, new BlockTypeInstructionEncoder()),
            new(
                WasmInstructionOperandShape.BranchTable,
                new BranchTableInstructionEncoder(unsigned)),
            new(
                WasmInstructionOperandShape.UnsignedLeb128,
                new UnsignedLeb128InstructionEncoder(unsigned)),
            new(
                WasmInstructionOperandShape.SignedLeb128,
                new SignedLeb128InstructionEncoder(signed)),
            new(
                WasmInstructionOperandShape.SignedLeb12864,
                new SignedLeb12864InstructionEncoder(signed64)),
            new(WasmInstructionOperandShape.Float32, new Float32InstructionEncoder()),
            new(WasmInstructionOperandShape.Float64, new Float64InstructionEncoder()),
            new(WasmInstructionOperandShape.Bytes, new BytesInstructionEncoder()),
            new(
                WasmInstructionOperandShape.MemoryArgument,
                new MemoryArgumentInstructionEncoder(unsigned)),
            new(
                WasmInstructionOperandShape.PrefixedUnsignedLeb128,
                new PrefixedUnsignedLeb128InstructionEncoder(unsigned)),
            new(
                WasmInstructionOperandShape.PrefixedUnsignedLeb128Pair,
                new PrefixedUnsignedLeb128PairInstructionEncoder(unsigned)),
            new(
                WasmInstructionOperandShape.PrefixedUnsignedLeb128Triple,
                new PrefixedUnsignedLeb128TripleInstructionEncoder(unsigned)),
            new(
                WasmInstructionOperandShape.TryTableCatch,
                new TryTableCatchInstructionEncoder(unsigned)),
        ]);
    }
}
