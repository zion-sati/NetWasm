namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed record JavaScriptImportResultEmitterRegistration(
    JavaScriptImportResultKind Kind,
    IJavaScriptImportResultEmitter Emitter);
