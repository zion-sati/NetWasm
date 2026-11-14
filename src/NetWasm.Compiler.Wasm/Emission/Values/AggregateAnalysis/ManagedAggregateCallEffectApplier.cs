using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal sealed class ManagedAggregateCallEffectApplier(
    IRuntimeIntrinsicRegistry intrinsics,
    IValueLayoutProvider values,
    ITargetLayout layouts) : IManagedAggregateCallEffectApplier
{
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));

    public ManagedAggregateAddressOrigins Apply(
        CilInstruction instruction,
        IReadOnlyList<ManagedAggregateAddressOrigins> stack,
        int consumed,
        ManagedAggregateAnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(session);
        var effects = session.Request.ByRefEffects;
        if (instruction.Operand is not CilOperand.MethodInstance operand)
        {
            session.RejectConsumed(stack, consumed);
            return ManagedAggregateAddressOrigins.None;
        }
        if (_intrinsics.TryGetIntrinsic(
                operand.Value.Definition.Key,
                out var intrinsic))
        {
            if (IsAliasPreservingIntrinsic(intrinsic))
            {
                var expectedConsumed = operand.Value.Signature.ParameterTypes.Length +
                    (operand.Value.Definition.IsStatic ? 0 : 1);
                if (consumed != 1 || consumed != expectedConsumed)
                {
                    session.RejectConsumed(stack, consumed);
                    return ManagedAggregateAddressOrigins.UnknownOnly;
                }
                var firstAlias = stack.Count - consumed;
                var origins = stack[firstAlias];
                var returnType = operand.Value.Signature.ReturnSignatureType;
                if (returnType.Shape != CliTypeShape.ManagedByReference ||
                    returnType.ElementType is not { } elementType ||
                    !TryGetAliasViewSize(elementType, out var viewSize) ||
                    origins.Recipes.Any(recipe =>
                        viewSize > session.Storages[recipe.Storage].Size - recipe.ByteOffset))
                {
                    session.Reject(origins);
                    return ManagedAggregateAddressOrigins.UnknownOnly;
                }
                return new(
                    origins.Unknown,
                    [.. origins.Recipes.Select(recipe =>
                        recipe with { Type = elementType })]);
            }
            if (operand.Value.Signature.ReturnType == CliValueKind.ManagedAddress)
            {
                session.RejectConsumed(stack, consumed);
                return ManagedAggregateAddressOrigins.UnknownOnly;
            }
        }
        if (
            !effects.TryGetValue(
                operand.Value.CanonicalName,
                out var targetEffects))
        {
            session.RejectConsumed(stack, consumed);
            return ManagedAggregateAddressOrigins.None;
        }

        if (operand.Value.Signature.ReturnType == CliValueKind.ManagedAddress)
        {
            // Ordinary managed-byref call summaries record only may-alias
            // argument indices. They do not prove exact identity, offset, or
            // whether another path can return external storage.
            session.RejectConsumed(stack, consumed);
            return ManagedAggregateAddressOrigins.UnknownOnly;
        }

        var first = stack.Count - consumed;
        for (var index = 0; index < consumed; index++)
        {
            var origins = stack[first + index];
            if (origins.Recipes.IsEmpty)
            {
                continue;
            }
            var argument = index;
            if (targetEffects.EscapedArguments.Contains(argument))
            {
                session.Reject(origins);
                continue;
            }
            foreach (var storage in origins.Recipes
                         .Select(recipe => recipe.Storage)
                         .Distinct())
            {
                session.Materializations.Add(new(instruction.Offset, storage,
                    targetEffects.WrittenArguments.Contains(argument)));
            }
        }
        return ManagedAggregateAddressOrigins.None;
    }

    private bool TryGetAliasViewSize(CliTypeIdentity type, out int size)
    {
        if (type.StackKind == CliValueKind.ValueType)
        {
            var layout = _values.GetValueLayout(type);
            size = layout.Size;
            return size > 0 &&
                layout.ReferenceOffsets.IsEmpty &&
                layout.ByReferenceOffsets.IsEmpty;
        }
        if (type.StackKind is CliValueKind.I4 or CliValueKind.I8 or
            CliValueKind.F4 or CliValueKind.F8 or CliValueKind.NativeInt)
        {
            size = _layouts.Target.GetStorageSize(type);
            return size > 0;
        }
        size = 0;
        return false;
    }

    private static bool IsAliasPreservingIntrinsic(RuntimeIntrinsic intrinsic) =>
        intrinsic is RuntimeIntrinsic.UnsafeAs or RuntimeIntrinsic.UnsafeAsRef or
            RuntimeIntrinsic.UnsafeAsRefManaged;
}
