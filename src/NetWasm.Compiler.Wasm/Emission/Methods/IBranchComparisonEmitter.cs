// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IBranchComparisonEmitter
{
    void Compare(
        IWasmInstructionWriter code,
        CilOperation operation,
        CliValueKind leftType,
        CliValueKind rightType);
}
