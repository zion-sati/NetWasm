// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IManagedTerminalExceptionBoundaryEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        int exceptionLocal,
        int rootFrameLocal,
        int typeIdLocal,
        int messageLocal,
        int messageLengthLocal,
        int stackTraceLocal,
        int stackTraceLengthLocal,
        CliValueKind resultType,
        int resultLocal,
        int reportFunctionIndex,
        int? raiseFunctionIndex,
        Action emitBody,
        // Called with the rooted exception in the catch, before it is reported; nothing on the body's normal path.
        int? exceptionObserverFunctionIndex = null);
}
