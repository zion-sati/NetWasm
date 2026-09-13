namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed record CallEmissionRegistration(
    CallEmissionKind Kind,
    ICallEmitter Emitter);
