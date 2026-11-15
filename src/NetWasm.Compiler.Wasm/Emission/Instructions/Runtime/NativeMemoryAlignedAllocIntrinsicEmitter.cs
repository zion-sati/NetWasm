// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class NativeMemoryAlignedAllocIntrinsicEmitter(
    INativeMemoryAllocationEmitter allocations) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code) =>
        allocations.Emit(request, code, RuntimeImportSymbol.NativeAlignedAlloc, 2);
}
