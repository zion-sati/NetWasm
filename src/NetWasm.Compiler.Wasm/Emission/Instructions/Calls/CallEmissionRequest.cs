using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed record CallEmissionRequest(
    InstructionEmissionRequest Instruction,
    MethodInstanceModel Method,
    int ArgumentBase,
    int Consumed,
    CliTypeIdentity? ConstrainedType = null);
