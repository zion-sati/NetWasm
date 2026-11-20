// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

// Passes a callback whose import returns no JSSubscription. JavaScript owns its handle, as a delegate passed through
// .NET's browser runtime belongs to JavaScript: the host releases it when JavaScript collects the function or the
// import fails.
internal interface ITransferredHostCallbackEmitter
{
    void Emit(IWasmInstructionWriter code, int delegateLocal);
}
