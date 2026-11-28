// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// What a lowered activation's guard is built from. Lowered: the runtime-async method; Activation: the method whose body
// is being built (the method itself, or its separate body). PassesThrough: a direct call's exception goes to its caller
// with no handler of its own in the first pass. Catch: the shape of the catch, whose region starts at TryStart.
internal sealed record RuntimeAsyncGuardRequest(
    MethodInstanceModel Lowered,
    MethodInstanceModel Activation,
    CilBodyLocal Mode,
    CilBodyLocal Direct,
    bool PassesThrough,
    bool IsIterator,
    CilOperand.FieldInstance ContextsChanged,
    CilBodyLabel TryStart,
    EntityKey ExceptionType,
    CliTypeIdentity ExceptionTypeIdentity,
    RuntimeAsyncCatchShape Catch);

// How a lowered activation takes its call mode (TakesMode, which also sets `direct` when UsesDirect), and the catch that
// guards its body: the handler (Catch), the faulted task's exit (FaultExit) and the region over the body, or none.
internal sealed record RuntimeAsyncGuard(
    ImmutableArray<CilBodyElement> TakesMode,
    ImmutableArray<CilBodyElement> Catch,
    ImmutableArray<CilBodyElement> FaultExit,
    CilBodyRegion? Region,
    bool UsesDirect);

internal interface IRuntimeAsyncActivationGuard
{
    // How the activation takes its call mode and what guards its body.
    RuntimeAsyncGuard Guard(RuntimeAsyncGuardRequest request);
}

internal interface IRuntimeAsyncActivationGuardSelector
{
    // The guard of the activation a body is being built for.
    IRuntimeAsyncActivationGuard Select(MethodInstanceModel activation);
}

// A runtime-async method that runs its own lowered body (a rewritten state machine's kickoff, an async iterator's member):
// it takes its call mode, naming itself by its function pointer, and guards its whole body with the catch.
internal sealed class SelfGuardedActivation(Func<IRuntimeAsyncMemberResolver> members, IRuntimeAsyncCatchEmitter catches)
    : IRuntimeAsyncActivationGuard
{
    private readonly Lazy<IRuntimeAsyncMemberResolver> _members = new(members);

    public RuntimeAsyncGuard Guard(RuntimeAsyncGuardRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var runtime = _members.Value;
        var overrides = request.Lowered.Definition.IsVirtual;
        List<CilBodyElement> takesMode =
        [
            Op(CilOperation.LoadFunction, new CilOperand.MethodInstance(request.Lowered)),
            Call(runtime.Resolve(RuntimeAsyncMember.Helper(overrides ? "TakeVirtualCallMode" : "TakeCallMode"))),
            Store(request.Mode),
        ];
        if (request.PassesThrough)
        {
            takesMode.AddRange([
                Load(request.Mode), Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), Op(CilOperation.BitwiseAnd),
                Store(request.Direct),
            ]);
        }
        if (request.IsIterator)
        {
            // An iterator's catch stays a handler while the program has changed an ambient context.
            var directKept = new CilBodyLabel();
            takesMode.AddRange([
                Op(CilOperation.LoadStaticField, request.ContextsChanged), Branch(CilOperation.BranchIfFalse, directKept),
                Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)), Store(request.Direct), Mark(directKept),
            ]);
        }
        var shape = request.Catch with { Direct = request.PassesThrough ? request.Direct : null };
        return new(
            [.. takesMode],
            catches.Emit(shape),
            [Mark(shape.FaultExit), Load(shape.FaultedTask), Op(CilOperation.Return)],
            new CilBodyRegion(CilExceptionRegionKind.Catch, request.TryStart, shape.Handler, shape.Handler, shape.HandlerEnd)
            {
                CatchType = request.ExceptionType,
                CatchTypeIdentity = request.ExceptionTypeIdentity,
                PassThroughLocal = request.PassesThrough ? request.Direct : null,
            },
            request.PassesThrough);
    }
}

// A runtime-async method's separate body (RuntimeAsyncBodies): its entry took the call mode and passes it as the last
// argument, and the entry's catch guards the call, so the body has none.
internal sealed class EntryGuardedBody : IRuntimeAsyncActivationGuard
{
    public RuntimeAsyncGuard Guard(RuntimeAsyncGuardRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var mode = (request.Activation.Definition.IsStatic ? 0 : 1) + request.Activation.Signature.ParameterSignatureTypes.Length - 1;
        return new(
            [Op(CilOperation.LoadArgument, new CilOperand.Index(mode)), Store(request.Mode)],
            [],
            [],
            null,
            false);
    }
}

// The body's guard when the method runs a separate body, the method's own otherwise.
internal sealed class RuntimeAsyncActivationGuardSelector(IRuntimeAsyncActivationGuard self, IRuntimeAsyncActivationGuard body)
    : IRuntimeAsyncActivationGuardSelector
{
    public IRuntimeAsyncActivationGuard Select(MethodInstanceModel activation) =>
        activation.Definition.RuntimeAsyncBodyOf is null ? self : body;
}
