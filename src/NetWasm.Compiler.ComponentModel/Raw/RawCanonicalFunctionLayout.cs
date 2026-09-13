using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawCanonicalFunctionLayout(
    WasmTarget Target,
    CanonicalAbiFunction Function,
    string Module,
    string Name,
    CanonicalAbiCoreSignature Signature,
    CanonicalAbiMemoryLayout ParameterMemory,
    CanonicalAbiMemoryLayout? ResultMemory);
