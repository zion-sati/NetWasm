// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class TerminalBranchJoinSelector(IBranchJoinSelector fallback) : IBranchJoinSelector
{
    private readonly IBranchJoinSelector _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));

    public BranchJoinSelection Select(BranchJoinSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.State);
        ArgumentNullException.ThrowIfNull(request.AllowedBlocks);
        var transfers = ImmutableHashSet.CreateBuilder<int>();
        if (request.ContinueTarget is int continueTarget)
            transfers.Add(continueTarget);
        if (request.BreakTarget is int breakTarget)
            transfers.Add(breakTarget);
        var terminals = transfers.ToImmutable();
        var allowed = request.AllowedBlocks.Except(terminals);
        if (request.First != request.Second &&
            allowed.Contains(request.First) && allowed.Contains(request.Second))
        {
            if (Terminates(request, request.First, request.Second, allowed))
                return new(request.Second, allowed, terminals, []);
            if (Terminates(request, request.Second, request.First, allowed))
                return new(request.First, allowed, terminals, []);
        }
        return _fallback.Select(request);
    }

    private static bool Terminates(BranchJoinSelectionRequest request, int start,
        int continuation, ImmutableHashSet<int> allowed)
    {
        var state = request.State;
        var seen = new HashSet<int>();
        var current = start;
        while (current != continuation && current != request.Stop && allowed.Contains(current) && seen.Add(current))
        {
            if (state.ExceptionGroupsByEntry.ContainsKey(current) ||
                state.LoopsByHeader.ContainsKey(current) ||
                state.DispatchersByNode.ContainsKey(current) ||
                !state.Graph.ExceptionalSuccessors[current].IsEmpty)
                return false;
            var block = state.Graph.GetBlock(current);
            var shared = state.Graph.Predecessors[current].Length > 1;
            if (shared && !IsRepeatableReturn(block))
                return false;
            if (block.Terminator.Operation is CilOperation.Return or CilOperation.Throw)
                return true;
            var successors = state.Graph.Successors[current];
            if (successors.Length != 1)
                return false;
            current = successors[0];
        }
        return false;
    }

    private static bool IsRepeatableReturn(BasicBlock block) =>
        block.Terminator.Operation == CilOperation.Return &&
        block.Instructions.Take(block.Instructions.Length - 1).All(instruction =>
            instruction.Operation is CilOperation.Nop or CilOperation.LoadLocal or
                CilOperation.LoadArgument or CilOperation.LoadInt32 or CilOperation.LoadInt64 or
                CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or CilOperation.LoadNull);
}
