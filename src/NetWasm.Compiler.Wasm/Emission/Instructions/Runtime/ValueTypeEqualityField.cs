using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal readonly record struct ValueTypeEqualityField(
    int Offset,
    CliTypeIdentity Type);
