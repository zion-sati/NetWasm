using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal readonly record struct ConditionValue(int Slot, CliValueKind Type);
