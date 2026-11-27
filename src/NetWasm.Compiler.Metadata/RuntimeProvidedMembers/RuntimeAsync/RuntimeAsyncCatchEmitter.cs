// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// What a lowered activation's catch works with: its labels, its locals, and which of its steps the activation needs.
// ClearsDirect: a direct call inside may have thrown before its callee took the request. Direct: the local that, while
// set, passes the exception on to a direct caller. ResumedExit: where a resumed activation leaves once it has recorded its
// fault (null when it is never resumed). Restore: the elements that put the caller's contexts back.
internal sealed record RuntimeAsyncCatchShape(
    CilBodyLabel Handler,
    CilBodyLabel HandlerEnd,
    CilBodyLocal Exception,
    CilBodyLocal Mode,
    bool ClearsDirect,
    ImmutableArray<CilBodyElement> Restore,
    CilBodyLocal? Direct,
    CilBodyLabel? ResumedExit,
    MethodInstanceModel Faulted,
    CilBodyLocal FaultedTask,
    CilBodyLabel FaultExit);

internal interface IRuntimeAsyncCatchEmitter
{
    // The handler of the catch that guards a lowered activation.
    ImmutableArray<CilBodyElement> Emit(RuntimeAsyncCatchShape shape);
}

// A resumed activation's exception goes to the frame that awaited it: the driver takes it from the fault slot, so the
// activation records it and leaves as a suspension does, and the driver resumes with no protected block of its own. A
// direct call's goes up its caller's stack, and a Normal call's into the task it returns.
internal sealed class RuntimeAsyncCatchEmitter(Func<IRuntimeAsyncMemberResolver> members) : IRuntimeAsyncCatchEmitter
{
    private const int ResumedMode = 2;

    private readonly Lazy<IRuntimeAsyncMemberResolver> _members = new(members);

    public ImmutableArray<CilBodyElement> Emit(RuntimeAsyncCatchShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var runtime = _members.Value;
        var rethrow = new CilBodyLabel();
        var notResumed = new CilBodyLabel();
        List<CilBodyElement> elements = [Mark(shape.Handler), Store(shape.Exception)];
        if (shape.ClearsDirect) elements.Add(Call(runtime.Resolve(RuntimeAsyncMember.Helper("ClearDirect"))));
        elements.AddRange(shape.Restore);
        if (shape.Direct is { } direct) elements.AddRange([Load(direct), Branch(CilOperation.BranchIfTrue, rethrow)]);
        if (shape.ResumedExit is { } resumedExit)
        {
            elements.AddRange([
                Load(shape.Mode),
                Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(ResumedMode)),
                Branch(CilOperation.BranchIfNotEqual, notResumed),
                Load(shape.Exception),
                Call(runtime.Resolve(RuntimeAsyncMember.Helper("Fault"))),
                Branch(CilOperation.Leave, resumedExit),
            ]);
        }
        elements.AddRange([
            Mark(notResumed),
            Load(shape.Mode),
            Branch(CilOperation.BranchIfTrue, rethrow),
            Load(shape.Exception),
            Call(shape.Faulted),
            Store(shape.FaultedTask),
            Branch(CilOperation.Leave, shape.FaultExit),
            Mark(rethrow),
            Op(CilOperation.Rethrow),
            Mark(shape.HandlerEnd),
        ]);
        return [.. elements];
    }
}
