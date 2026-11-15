// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
