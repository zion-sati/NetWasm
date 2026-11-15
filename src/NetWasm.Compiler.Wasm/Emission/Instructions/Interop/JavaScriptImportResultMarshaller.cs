// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Encoding;
namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed class JavaScriptImportResultMarshaller(
    IJavaScriptImportResultKindResolver kinds,
    IJavaScriptImportResultEmitterRegistry emitters) :
    IJavaScriptImportResultEmitter
{
    public void Emit(JavaScriptImportResultRequest request, IWasmInstructionWriter code) =>
        emitters.Get(kinds.Resolve(request.Signature.ReturnSignatureType)).Emit(request, code);
}
