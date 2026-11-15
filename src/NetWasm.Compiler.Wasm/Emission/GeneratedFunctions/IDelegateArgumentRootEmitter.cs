// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IDelegateArgumentRootEmitter
{
    // Publishes the delegate and the references its arguments hold in a new root frame,
    // whose address the frame local then holds.
    void Emit(IWasmInstructionWriter code, MethodInstanceModel invoke, int frameLocal);
}
