using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal interface IManagedAggregateFieldResolver
{
    // Resolves a field operand to an instance field the aggregate type declares, with its layout and type.
    bool TryResolve(
        CliTypeIdentity aggregateType,
        CilOperand operand,
        out FieldLayout layout,
        out CliTypeIdentity fieldType);
}
