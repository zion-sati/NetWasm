// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IReferenceComparisonEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        ReferenceComparison comparison,
        CliValueKind operandType = CliValueKind.ManagedReference);
}
