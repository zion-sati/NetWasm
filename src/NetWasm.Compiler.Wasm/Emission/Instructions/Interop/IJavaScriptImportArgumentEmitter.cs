// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

// Pushes one import argument's Wasm values from the local that holds it. It takes no method context: the call emitters
// keep their own values in the context's locals while the arguments are emitted (an async import its task's handle, a
// subscription its callback's), so an argument must not have a local of theirs to write.
internal interface IJavaScriptImportArgumentEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        CliTypeIdentity type,
        int local,
        InteropImportPlan interop);
}
