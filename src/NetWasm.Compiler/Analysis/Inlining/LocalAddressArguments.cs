// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Inlining;

// The positions, among the callee's parameters, of a call's arguments that are the address of one of the caller's
// locals or arguments: what the priority tier weighs a by-reference callee past its budget by, both where the inliner
// meets the call and where the resolver judges a wrapper whose own body makes it (see
// ManagedInliningResolver.ByReferenceBodyILBytes).
internal static class LocalAddressArguments
{
    public static ImmutableHashSet<int> Of(
        CilMethodBody body,
        int callerIndex,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> instructionEntryStacks,
        StackSlotProducers producers) =>
        body.Instructions[callerIndex].Operand is CilOperand.MethodInstance { Value: var method }
            ? Of(body, callerIndex, method, instructionEntryStacks, producers)
            : [];

    // The same, for a call whose callee is already resolved.
    public static ImmutableHashSet<int> Of(
        CilMethodBody body,
        int callerIndex,
        MethodInstanceModel method,
        IReadOnlyDictionary<int, ImmutableArray<CliValueKind>> instructionEntryStacks,
        StackSlotProducers producers)
    {
        var call = body.Instructions[callerIndex];
        if (!instructionEntryStacks.TryGetValue(call.Offset, out var stack))
        {
            return [];
        }
        var arguments = method.Signature.ParameterTypes.Length +
            (method.Definition.IsStatic || call.Operation == CilOperation.NewObject ? 0 : 1);
        // The callee's parameters count its instance, which newobj does not take from the stack.
        var first = stack.Length - arguments - (call.Operation == CilOperation.NewObject ? 1 : 0);
        var found = Enumerable.Range(Math.Max(0, stack.Length - arguments), Math.Min(arguments, stack.Length)).Where(depth =>
                producers.Find(callerIndex, depth) is { } producer &&
                body.Instructions[producer].Operation is CilOperation.LoadLocalAddress or CilOperation.LoadArgumentAddress)
            .Select(depth => depth - first)
            .ToImmutableHashSet();
        // A value type's newobj constructs its instance in a temporary of the caller, whose address the
        // constructor receives.
        return call.Operation == CilOperation.NewObject && method.DeclaringType.IsValueType ? found.Add(0) : found;
    }
}
