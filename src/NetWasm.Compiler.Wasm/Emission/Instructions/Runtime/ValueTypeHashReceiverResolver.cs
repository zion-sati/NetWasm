using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Strategy: prove the storage type and representation consumed by default value hashing.
internal sealed class ValueTypeHashReceiverResolver(
    ICilTypeOperandResolver types,
    INullableTypeResolver nullableTypes) : IValueTypeHashReceiverResolver
{
    public ValueTypeHashReceiver Resolve(RuntimeIntrinsicEmissionRequest request)
    {
        if (request.ConstrainedType is { } constrained)
            return new(constrained, CliValueKind.ManagedAddress);

        var instruction = request.Instruction;
        var body = instruction.Header.Instructions;
        var index = body.IndexOf(instruction.Instruction);
        if (request.Call.Consumed != 1 ||
            instruction.Stack[request.ArgumentBase] != CliValueKind.ManagedReference ||
            index <= 0)
            throw UnknownReceiver();

        var producer = body[index - 1];
        if (producer.Operation != CilOperation.Box ||
            producer.NextOffset != instruction.Instruction.Offset)
            throw UnknownReceiver();

        // A lexical predecessor is not proof if another path can skip the box.
        foreach (var candidate in body)
        {
            if (candidate.Operand is CilOperand.BranchTarget branch &&
                branch.Offset == instruction.Instruction.Offset)
                throw UnknownReceiver();
            if (candidate.Operand is CilOperand.SwitchTargets targets &&
                targets.Offsets.Contains(instruction.Instruction.Offset))
                throw UnknownReceiver();
        }

        var type = types.Resolve(producer, instruction.Header.MethodInstance);
        // Boxing Nullable<T> produces a boxed T or null, not a Nullable<T> payload.
        if (!type.IsValueType || nullableTypes.Resolve(type) is not null)
            throw UnknownReceiver();
        return new(type, CliValueKind.ManagedReference);
    }

    private static InvalidOperationException UnknownReceiver() => new(
        "Default value hashing requires a constrained receiver or a proven boxed value type.");
}
