using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record FilterFunclet(
    int Id,
    ManagedMethodIdentity CallerIdentity,
    StructuredMethod Method,
    StructuredExceptionClause Clause);

internal readonly record struct ExceptionGroupMetadata(
    int Address,
    int ClauseCount,
    bool HasFilters);
