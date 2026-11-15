// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.CallLifetimes;

internal enum ScalarCallPreservationPhase
{
    BeforeCall,
    AfterNormalReturn,
}

internal interface IScalarCallPreservationEmitter
{
    void Emit(InstructionEmissionRequest request, IWasmInstructionWriter code, ScalarCallPreservationPhase phase);
}
