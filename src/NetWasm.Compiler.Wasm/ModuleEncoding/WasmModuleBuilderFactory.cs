using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.ModuleEncoding;

internal static class WasmModuleBuilderFactory
{
    public static WasmModuleBuilder Create()
    {
        var unsigned = new UnsignedLeb128Encoder();
        var signed = new SignedLeb128Encoder();
        var signed64 = new SignedLeb12864Encoder();
        var instructionEncoders = new WasmInstructionEncoderRegistry(
        [
            new(
                WasmInstructionOperandShape.None,
                new NoOperandInstructionEncoder()),
            new(
                WasmInstructionOperandShape.SignedLeb128,
                new SignedLeb128InstructionEncoder(signed)),
            new(
                WasmInstructionOperandShape.SignedLeb12864,
                new SignedLeb12864InstructionEncoder(signed64)),
        ]);
        return new WasmModuleBuilder(
            new ModuleEncoder(
                unsigned,
                new UnsignedLeb12864Encoder(),
                signed,
                signed64,
                new Utf8StringEncoder(unsigned),
                new WasmSectionWriter(unsigned),
                static buffer => new WasmBinaryWriter(buffer),
                static buffer => new WasmBinarySnapshotReader(buffer),
                output => new WasmInstructionWriter(output, instructionEncoders)));
    }
}
