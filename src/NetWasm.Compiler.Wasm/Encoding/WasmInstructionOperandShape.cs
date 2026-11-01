namespace NetWasm.Compiler.Wasm.Encoding;

#pragma warning disable CA1720 // WebAssembly operand shapes intentionally name binary value types.
/// <summary>
/// Describes the binary shape of an instruction operand.
/// </summary>
public enum WasmInstructionOperandShape
{
    None = 0,
    UnsignedLeb128 = 2,
    SignedLeb128 = 3,
    SignedLeb12864 = 5,
    Float32 = 6,
    Float64 = 7,
    Bytes = 8,
    MemoryArgument = 10,
    PrefixedUnsignedLeb128 = 11,
    PrefixedUnsignedLeb128Pair = 12,
    TryTableCatch = 13,
    PrefixedUnsignedLeb128Triple = 14,
    BlockType = 15,
    BranchTable = 16,
}
#pragma warning restore CA1720
