// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync.RuntimeAsyncAwaits;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

internal interface IRuntimeAsyncEntryBuilder
{
    // The entry of a runtime-async method that runs a separate body (RuntimeAsyncBodies).
    CilMethodBody Build(MethodInstanceModel method);
}

// A runtime-async method's entry, when its lowered body runs in a method beside it:
//   mode = TakeCallMode()
//   if (mode is DirectVirtual && !ContextsMayChange()) return body(arguments, mode)     (a virtual method's only)
//   direct = mode & 1; contexts = CaptureContexts()
//   try { returned = body(arguments, mode) }
//   catch (Exception e) { ClearDirect; RestoreContexts; a direct call rethrows (passed by in the first pass); a resumed
//     one records its fault and leaves as a suspension does; a Normal call returns a faulted task; the others rethrow }
//   returned
// This is the catch the lowering wrapped the whole body in (RuntimeAsyncBodyBuilder), with the same semantics: only the
// call of the body is inside it. The body captures the contexts again for its own exits; nothing runs between the two
// captures. The handler drops a direct request whatever the body holds: it cannot tell whether the body made one.
// A direct call needs the catch only to put its caller's contexts back, which a program that changes none (the compiler
// folds RuntimeAsync.ContextsMayChange then) never needs, so the call runs the body with no protected block at all: a
// request the body made and its callee never took is dropped by the handler or filter of the caller's protected block
// around the call, or by the nearest entry's catch, before any other code runs, as the method's own catch did. Only a
// virtual method's entry takes a direct call: every other direct call that a context cannot change calls the body itself
// (RuntimeAsyncDirectSiteEmitter), so a direct call reaches any other entry only where ContextsMayChange holds. The body
// of a method no direct call reaches then has the one caller, which the optimizer inlines.
internal sealed class RuntimeAsyncEntryBuilder(
    Func<IRuntimeAsyncMemberResolver> members,
    IMethodRepository methods,
    ICilBodyLayout layout,
    IRuntimeAsyncCatchEmitter catches,
    ISymbolFormatter symbols) : IRuntimeAsyncEntryBuilder
{
    private readonly Lazy<IRuntimeAsyncMemberResolver> _members = new(members);

    public CilMethodBody Build(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var runtime = _members.Value;
        var returnType = method.Signature.ReturnSignatureType;
        var (shape, result) = Shape(returnType) ?? throw new CompilerException(new CompilerDiagnostic(
            DiagnosticCode.UnsupportedCil,
            "is a runtime-async method (MethodImplOptions.Async) that returns neither a Task nor a ValueTask",
            symbols.Format(method.Definition)));
        var overrides = method.Definition.IsVirtual;
        var body = RuntimeAsyncBodies.For(methods, method);
        var mode = new CilBodyLocal(runtime.Resolve(RuntimeAsyncMember.Helper("TakeCallMode")).Signature.ReturnSignatureType);
        var direct = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var execution = new CilBodyLocal(runtime.Resolve(RuntimeAsyncMember.Helper("CaptureExecution")).Signature.ReturnSignatureType);
        var token = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var synchronization = new CilBodyLocal(
            runtime.Resolve(RuntimeAsyncMember.Helper("CaptureSynchronization")).Signature.ReturnSignatureType);
        var returned = new CilBodyLocal(returnType);
        var exception = new CilBodyLocal(CliTypeIdentity.FromDefinition(runtime.Exception));
        var faulted = new CilBodyLocal(returnType);
        var contextsChanged = new CilOperand.FieldInstance(runtime.ContextsChanged);
        var captured = new CilBodyLabel();
        var guarded = new CilBodyLabel();
        var tryStart = new CilBodyLabel();
        var handler = new CilBodyLabel();
        var handlerEnd = new CilBodyLabel();
        var done = new CilBodyLabel();
        var resumedExit = new CilBodyLabel();
        var faultExit = new CilBodyLabel();

        // Puts the caller's contexts back, unless the program has changed none.
        IEnumerable<CilBodyElement> RestoreContexts(string helper)
        {
            var restored = new CilBodyLabel();
            return [Op(CilOperation.LoadStaticField, contextsChanged), Branch(CilOperation.BranchIfFalse, restored),
                Load(mode), Load(execution), Load(token), Load(synchronization),
                Call(runtime.Resolve(RuntimeAsyncMember.Helper(helper))), Mark(restored)];
        }

        var firstArgument = method.Definition.IsStatic ? 0 : 1;
        var arguments = firstArgument + method.Signature.ParameterSignatureTypes.Length;
        // The body's call: the arguments as they came, and the mode.
        IEnumerable<CilBodyElement> CallBody()
        {
            for (var argument = 0; argument < arguments; argument++)
                yield return Op(CilOperation.LoadArgument, new CilOperand.Index(argument));
            yield return Load(mode);
            yield return Call(body);
        }
        List<CilBodyElement> elements =
        [
            Op(CilOperation.LoadFunction, new CilOperand.MethodInstance(method)),
            Call(runtime.Resolve(RuntimeAsyncMember.Helper(overrides ? "TakeVirtualCallMode" : "TakeCallMode"))),
            Store(mode),
            .. overrides
                ? [Load(mode), Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), Op(CilOperation.BitwiseAnd),
                    Branch(CilOperation.BranchIfFalse, guarded),
                    Call(runtime.Resolve(RuntimeAsyncMember.Helper("ContextsMayChange"))),
                    Branch(CilOperation.BranchIfTrue, guarded),
                    .. CallBody(),
                    Op(CilOperation.Return)]
                : Array.Empty<CilBodyElement>(),
            Mark(guarded),
            Load(mode), Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(1)), Op(CilOperation.BitwiseAnd), Store(direct),
            Op(CilOperation.LoadStaticField, contextsChanged),
            Branch(CilOperation.BranchIfFalse, captured),
            Call(runtime.Resolve(RuntimeAsyncMember.Helper("CaptureExecution"))),
            Store(execution),
            Call(runtime.Resolve(RuntimeAsyncMember.Helper("CapturedToken"))),
            Store(token),
            Call(runtime.Resolve(RuntimeAsyncMember.Helper("CaptureSynchronization"))),
            Store(synchronization),
            // The guard's target stays outside the protected block, which is entered at its start.
            Mark(captured),
            Op(CilOperation.Nop),
            Mark(tryStart),
        ];
        elements.AddRange([
            .. CallBody(),
            Store(returned),
            Branch(CilOperation.Leave, done),
            .. catches.Emit(new RuntimeAsyncCatchShape(
                handler,
                handlerEnd,
                exception,
                mode,
                true,
                [.. RestoreContexts("RestoreContexts")],
                direct,
                resumedExit,
                RuntimeAsyncCompletions.Resolve(runtime, "Faulted", shape, result),
                faulted,
                faultExit)),
            Mark(done),
            Load(returned),
            Op(CilOperation.Return),
            Mark(resumedExit),
            Load(mode),
            Call(RuntimeAsyncCompletions.Resolve(runtime, overrides ? "VirtualSuspended" : "Suspended", shape, result)),
            Store(returned),
            .. RestoreContexts("RestoreContextsOnSuspension"),
            Load(returned),
            Op(CilOperation.Return),
            Mark(faultExit),
            Load(faulted),
            Op(CilOperation.Return),
        ]);
        var draft = new CilBodyDraft(
            [mode, direct, execution, token, synchronization, returned, exception, faulted],
            [.. elements],
            [new CilBodyRegion(CilExceptionRegionKind.Catch, tryStart, handler, handler, handlerEnd)
            {
                CatchType = runtime.Exception.Key,
                CatchTypeIdentity = CliTypeIdentity.FromDefinition(runtime.Exception),
                PassThroughLocal = direct,
            }])
        {
            InitializesLocals = true,
        };
        // The arguments and the mode for the body's call; the mode and three contexts for a restore.
        return layout.Lay(draft, method, Math.Max(arguments + 1, 4));
    }
}
