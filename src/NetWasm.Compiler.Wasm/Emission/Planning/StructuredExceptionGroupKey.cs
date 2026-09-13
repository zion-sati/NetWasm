using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal readonly record struct StructuredExceptionGroupKey(
    EntityKey Method,
    string? MethodInstanceCanonicalName,
    StructuredExceptionGroupId Group);
