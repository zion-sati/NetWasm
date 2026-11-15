// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IManagedTerminalTrapBoundaryEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        CliValueKind resultType,
        int resultLocal,
        Action emitBody,
        int? unhandledExceptionReporter = null);
}
