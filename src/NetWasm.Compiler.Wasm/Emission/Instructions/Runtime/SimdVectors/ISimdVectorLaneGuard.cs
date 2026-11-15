// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.SimdVectors;

internal interface ISimdVectorLaneGuard
{
    // Resolves the lanes an intrinsic works on; for an element type the vectors do not support, emits the
    // NotSupportedException upstream's software implementation throws and returns false.
    bool TryResolve(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, out SimdVectorLanes lanes);
}
