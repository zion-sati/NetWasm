// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Arrays;

// A run of elements a versioned loop's test proves free of address 0: Length elements of Element from the managed address
// in local Reference. The loop's reads in Reads (Unsafe.Add calls by offset, with their element type) lie in it.
internal sealed record ArrayLoopNonZeroRun(int Reference, int Length, CliTypeIdentity Element);

internal sealed record ArrayLoopNonZeroReads(ImmutableArray<ArrayLoopNonZeroRun> Runs, ImmutableDictionary<int, CliTypeIdentity> Reads)
{
    public static ArrayLoopNonZeroReads None { get; } = new([], ImmutableDictionary<int, CliTypeIdentity>.Empty);
}

internal interface IArrayLoopNonZeroReadFinder
{
    // Checks maps each check the copy proves or still makes against a length the loop leaves alone (by offset) to its
    // block and the local holding its length; invariant says which locals the loop leaves alone.
    ArrayLoopNonZeroReads Find(ValidatedControlFlowGraph controlFlow, ArrayLoopInduction loop,
        IReadOnlyDictionary<int, (int Block, int Length)> checks, Func<int, bool> invariant);
}

// The reads a versioned loop's copy may take without checking their address. An inlined span indexer checks
// `(uint)x < (uint)length` and then, on the branch that passes and with nothing before it, reads Unsafe.Add(ref r,
// (nuint)x). In the copy every proven check passes, and a check it still makes throws before the read, so where the read
// runs x lies in [0, length); a test ahead of the loop that the run of length elements from r holds no address 0
// (NonZeroAddressRange) then proves r + x * size is not 0, whatever x's shape: `start + index - width`, or a value read
// from memory. That the length is r's own does not matter: the test is of the
// run it names. Each such read is found from its check; the passing branch's block must have the check's block as its
// only predecessor, so the read sees the x the check tested, and r must be a managed-address local the loop leaves
// alone.
internal sealed class ArrayLoopNonZeroReadFinder(ICalledMethodResolver calledMethods, IRuntimeIntrinsicRegistry intrinsics)
    : IArrayLoopNonZeroReadFinder
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));

    public ArrayLoopNonZeroReads Find(ValidatedControlFlowGraph controlFlow, ArrayLoopInduction loop,
        IReadOnlyDictionary<int, (int Block, int Length)> checks, Func<int, bool> invariant)
    {
        ArgumentNullException.ThrowIfNull(controlFlow);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(invariant);
        var graph = controlFlow.Graph;
        var locals = graph.MethodBody.Locals;
        var runs = new HashSet<ArrayLoopNonZeroRun>();
        var reads = ImmutableDictionary.CreateBuilder<int, CliTypeIdentity>();
        foreach (var (offset, (blockIndex, length)) in checks.OrderBy(static check => check.Key))
        {
            var block = graph.GetBlock(blockIndex).Instructions;
            if (block[^1] is not { Offset: var last } check || last != offset) continue;
            // `ldloc x; ldloc length; blt.un ok` (or bge.un fail), past nops.
            var lengthLoad = Previous(block, block.Length - 1);
            var indexLoad = Previous(block, lengthLoad);
            if (indexLoad < 0 ||
                block[lengthLoad] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var loaded } } ||
                loaded != length ||
                block[indexLoad] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var index } })
            {
                continue;
            }
            var passing = check.Operation switch
            {
                CilOperation.BranchIfLessThanUnsigned or CilOperation.BranchIfLessThanSigned =>
                    ((CilOperand.BranchTarget)check.Operand).Offset,
                CilOperation.BranchIfGreaterThanOrEqualUnsigned or CilOperation.BranchIfGreaterThanOrEqualSigned => check.NextOffset,
                _ => -1,
            };
            if (passing < 0) continue;
            var target = graph.GetBlockAtOffset(passing);
            if (!loop.LoopBlocks.Contains(target.Index) || graph.Predecessors[target.Index] is not [var only] ||
                only != blockIndex)
            {
                continue;
            }
            // `ldloc r; ldloc x; conv.u (or conv.i); call Unsafe.Add<T>(ref T, nint)`, first in the block.
            var code = target.Instructions;
            var at = Next(code, -1);
            var reference = at;
            var indexRead = Next(code, reference);
            var conversion = Next(code, indexRead);
            var add = Next(code, conversion);
            if (add >= code.Length ||
                code[reference] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var r } } ||
                locals[r] != CliValueKind.ManagedAddress || !invariant(r) ||
                code[indexRead] is not { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index { Value: var read } } ||
                read != index ||
                code[conversion].Operation is not (CilOperation.ConvertNativeUInt or CilOperation.ConvertNativeInt) ||
                code[add] is not { Operation: CilOperation.Call, Operand: CilOperand.MethodInstance { Value: var method } } call ||
                !_intrinsics.TryGetIntrinsic(CilOperands.MethodKey(call.Operand), out var intrinsic) ||
                intrinsic != RuntimeIntrinsic.UnsafeAdd ||
                _calledMethods.Resolve(call) is not
                {
                    MethodArguments: [_],
                    Signature.ParameterTypes: [CliValueKind.ManagedAddress, CliValueKind.NativeInt],
                })
            {
                continue;
            }
            var element = method.MethodArguments[0];
            runs.Add(new(r, length, element));
            reads[call.Offset] = element;
        }
        return reads.Count == 0
            ? ArrayLoopNonZeroReads.None
            : new([.. runs.OrderBy(static run => run.Reference).ThenBy(static run => run.Length)
                .ThenBy(static run => run.Element.CanonicalName, StringComparer.Ordinal)], reads.ToImmutable());
    }

    // The instruction before index, past nops, or -1.
    private static int Previous(ImmutableArray<CilInstruction> code, int index)
    {
        do index--;
        while (index >= 0 && code[index].Operation == CilOperation.Nop);
        return index;
    }

    // The instruction after index, past nops, or the block's length.
    private static int Next(ImmutableArray<CilInstruction> code, int index)
    {
        do index++;
        while (index < code.Length && code[index].Operation == CilOperation.Nop);
        return index;
    }
}
