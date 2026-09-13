using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed record RuntimeIntrinsicEmitterRegistration(
    RuntimeIntrinsic Intrinsic,
    IRuntimeIntrinsicEmitter Emitter);
