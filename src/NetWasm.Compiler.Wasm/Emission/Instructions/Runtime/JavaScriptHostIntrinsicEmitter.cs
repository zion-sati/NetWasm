// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Reports the compiled host flavour to CoreLib: modules behind the JavaScript export boundary
// (raw core modules and browser JavaScript modules) are JavaScript-hosted; WIT components are not.
internal sealed class JavaScriptHostIntrinsicEmitter : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var javaScriptHost = request.Instruction.Target.InteropImports.UseJavaScriptExportBoundary;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(javaScriptHost ? 1 : 0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Local(0, CliValueKind.I4)))));
    }
}
