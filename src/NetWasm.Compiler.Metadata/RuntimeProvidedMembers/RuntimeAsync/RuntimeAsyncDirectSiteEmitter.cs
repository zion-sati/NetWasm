// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// A direct await's call of a lowered method (Callee), as the method's body has it (Call, its arguments on the stack).
internal sealed record RuntimeAsyncDirectSite(MethodInstanceModel Callee, CilBodyElement.Instruction Call);

internal interface IRuntimeAsyncDirectSiteEmitter
{
    // The call a direct await makes of a lowered method, whose result goes to the slot rather than into a task.
    ImmutableArray<CilBodyElement> Emit(RuntimeAsyncDirectSite site);
}

// The callee takes the call as a direct one through a request naming it by its function pointer (CallDirect). A callee
// that runs a separate body (RuntimeAsyncBodies, RuntimeAsyncBodyPlacement) is called at its body, in Direct mode, with no request and no entry, where
// the program changes no ambient context (RuntimeAsync.ContextsMayChange, which the compiler folds): the entry's catch
// would only have put the caller's contexts back. The call keeps its opcode, so a callvirt still tests the receiver.
internal sealed class RuntimeAsyncDirectSiteEmitter(Func<IRuntimeAsyncMemberResolver> members, IMethodRepository methods,
    IRuntimeAsyncBodyPlacement bodies)
    : IRuntimeAsyncDirectSiteEmitter
{
    private const int DirectMode = 1;

    private readonly Lazy<IRuntimeAsyncMemberResolver> _members = new(members);

    public ImmutableArray<CilBodyElement> Emit(RuntimeAsyncDirectSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        var runtime = _members.Value;
        ImmutableArray<CilBodyElement> request =
        [
            Op(CilOperation.LoadFunction, new CilOperand.MethodInstance(site.Callee)),
            Call(runtime.Resolve(RuntimeAsyncMember.Helper("CallDirect"))),
            site.Call,
        ];
        if (!bodies.Separates(site.Callee.Definition)) return request;
        var viaEntry = new CilBodyLabel();
        var called = new CilBodyLabel();
        return
        [
            Call(runtime.Resolve(RuntimeAsyncMember.Helper("ContextsMayChange"))),
            Branch(CilOperation.BranchIfTrue, viaEntry),
            Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(DirectMode)),
            site.Call with { Operand = new CilOperand.MethodInstance(RuntimeAsyncBodies.For(methods, site.Callee)) },
            Branch(CilOperation.Branch, called),
            Mark(viaEntry),
            .. request,
            Mark(called),
        ];
    }
}
