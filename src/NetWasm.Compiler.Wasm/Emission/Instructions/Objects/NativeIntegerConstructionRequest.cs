using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed record NativeIntegerConstructionRequest(
    InstructionEmissionRequest Emission,
    MethodSignatureModel Signature,
    int ArgumentBase);
