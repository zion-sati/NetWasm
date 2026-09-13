using System;

namespace NetWasm.Compiler.Wasm.Encoding;

public sealed record WasmInstructionEncoderRegistration
{
    public WasmInstructionEncoderRegistration(
        WasmInstructionOperandShape shape,
        IWasmInstructionEncoder encoder)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        Shape = shape;
        Encoder = encoder;
    }

    public WasmInstructionOperandShape Shape { get; }

    public IWasmInstructionEncoder Encoder { get; }
}
