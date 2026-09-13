using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal sealed record InteropMarshallingTarget(InteropImportPlan Imports);
