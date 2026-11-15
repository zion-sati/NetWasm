// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Encoding;

public interface IUtf8StringEncoder
{
    void Encode(IWasmBinaryWriter writer, string value);
}
