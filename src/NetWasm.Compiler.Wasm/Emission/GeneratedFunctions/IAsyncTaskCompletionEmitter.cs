// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

// An asynchronous export's completion observes its task with await semantics; a fault's
// exception goes to the capture import, whose payload the completion returns.
internal sealed record AsyncTaskCompletionPlan(
    int ObserveFunctionIndex,
    int CaptureFunctionIndex,
    // A JavaScript export's offer of a JSException's error (JavaScriptConversion.ExportFailure), called in the catch.
    int? ExceptionObserverFunctionIndex = null);

internal interface IAsyncTaskCompletionEmitter
{
    byte[] Emit(JavaScriptAsyncMethodBinding binding, AsyncTaskCompletionPlan plan);
}
