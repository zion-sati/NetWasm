using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal sealed class ManagedAggregateObjectAccessRecorder(
    ICilTypeOperandResolver typeOperands,
    IValueLayoutProvider values) : IManagedAggregateObjectAccessRecorder
{
    private readonly ICilTypeOperandResolver _typeOperands = typeOperands ??
        throw new ArgumentNullException(nameof(typeOperands));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));

    public void Record(
        CilInstruction instruction,
        StructuredBlockId block,
        ManagedAggregateAddressOrigins origins,
        bool writes,
        ManagedAggregateAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(session);
        if (!session.TrySingleRecipe(origins, out var recipe))
        {
            return;
        }
        var type = _typeOperands.Resolve(instruction, session.Request.Method.Header.MethodInstance);
        if (type.StackKind is not (
                CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or
                CliValueKind.F8 or CliValueKind.NativeInt) ||
            recipe.Type.StackKind != type.StackKind)
        {
            session.Reject(recipe.Storage);
            return;
        }
        session.RecordAccess(
            instruction,
            block,
            recipe with { Type = type },
            _values.GetValueLayout(type).Size,
            type.StackKind,
            writes);
    }
}
