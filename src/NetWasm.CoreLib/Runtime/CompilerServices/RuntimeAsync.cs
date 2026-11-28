// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm: the runtime that NetWasm's runtime-async lowering calls. Roslyn's runtime-async=on compiles an async method
// without a state machine and leaves its suspension to the runtime; NetWasm's compiler rewrites such a method so that,
// at an await that cannot complete at once, it saves its arguments and locals into a RuntimeAsyncFrame, pushes the
// frame and returns, and on resumption restores them and continues after that await. The chain of frames, innermost
// first, is driven by RuntimeAsyncDriver<T> once the outermost caller that is not runtime-async asks for a Task. The
// program is single-threaded, so the call mode, the suspending flag and the result slots are statics.
// Contexts follow upstream's runtime async (AsyncHelpers.CoreCLR.cs): a call that is not a resumption puts its caller's
// ExecutionContext and SynchronizationContext back when it returns, throws or suspends; each frame resumes under the
// ExecutionContext it suspended with; the awaiter is subscribed under the leaf's contexts; a frame that awaited its
// callee with a SynchronizationContext of its own continues there.
// Upstream (dotnet/runtime v11.0.0, src/coreclr/System.Private.CoreLib/src/System/Runtime/CompilerServices/
// AsyncHelpers.CoreCLR.cs) is the reference for every piece here; where NetWasm's code is upstream's, it is copied, and
// where the lowering differs (frames the compiler lays out at the CIL level instead of the JIT's Continuation objects,
// a delegate to the method instead of ResumeInfo function pointers), each piece names its upstream counterpart.

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace System.Runtime.CompilerServices
{
    // One suspended activation of a lowered method: where it resumes, what it saved, and how it is called again.
    // Upstream's Continuation: the JIT makes one at each suspension, laying the saved values out inline in a type the VM
    // makes for that layout (async.cpp's ContinuationLayoutBuilder and getContinuationType), and resumes through
    // ResumeInfo->Resume. A NetWasm frame serves every suspension of its activation: its subclass for the method's arity
    // holds the method's delegate, which Resume calls, and its slots as typed fields (TSlots, a RuntimeAsyncSlots the
    // lowering instantiates with the types its suspensions keep), so a save is a field store and nothing is boxed.
    internal abstract class RuntimeAsyncFrame
    {
        internal int State;
        internal RuntimeAsyncFrame? Next;

        // What the frame resumes under: its ExecutionContext (null while flow was suppressed), and a
        // SynchronizationContext other than the default one, which an outer frame continues on.
        internal ExecutionContext? Execution;
        internal SynchronizationContext? Synchronization;

        // Calls the method again, with default arguments, which its prologue replaces with the saved ones.
        internal abstract void Resume();
    }

#pragma warning disable CS0649 // The lowered methods read and write the slots.
    internal sealed class RuntimeAsyncFrame<TSlots, TResult>(Func<TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method();
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, TResult>(Func<T1, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, TResult>(Func<T1, T2, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, TResult>(Func<T1, T2, T3, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, TResult>(Func<T1, T2, T3, T4, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, T5, TResult>(Func<T1, T2, T3, T4, T5, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, T5, T6, TResult>(Func<T1, T2, T3, T4, T5, T6, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, T5, T6, T7, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!, default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, T5, T6, T7, T8, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!, default!, default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!, default!, default!, default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!, default!, default!, default!, default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!);
    }

    internal sealed class RuntimeAsyncFrame<TSlots, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, TResult> method) : RuntimeAsyncFrame
        where TSlots : struct
    {
        internal TSlots Slots;

        internal override void Resume() => method(default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!, default!);
    }

    // A frame's slots, one field per value its suspensions keep, seven to a struct and the rest in Rest, as a
    // ValueTuple nests: upstream's inline continuation layout, with the types the lowering keeps rather than raw bytes.
    internal struct RuntimeAsyncSlots;

    internal struct RuntimeAsyncSlots<T1>
    {
        internal T1 Item1;
    }

    internal struct RuntimeAsyncSlots<T1, T2>
    {
        internal T1 Item1;
        internal T2 Item2;
    }

    internal struct RuntimeAsyncSlots<T1, T2, T3>
    {
        internal T1 Item1;
        internal T2 Item2;
        internal T3 Item3;
    }

    internal struct RuntimeAsyncSlots<T1, T2, T3, T4>
    {
        internal T1 Item1;
        internal T2 Item2;
        internal T3 Item3;
        internal T4 Item4;
    }

    internal struct RuntimeAsyncSlots<T1, T2, T3, T4, T5>
    {
        internal T1 Item1;
        internal T2 Item2;
        internal T3 Item3;
        internal T4 Item4;
        internal T5 Item5;
    }

    internal struct RuntimeAsyncSlots<T1, T2, T3, T4, T5, T6>
    {
        internal T1 Item1;
        internal T2 Item2;
        internal T3 Item3;
        internal T4 Item4;
        internal T5 Item5;
        internal T6 Item6;
    }

    internal struct RuntimeAsyncSlots<T1, T2, T3, T4, T5, T6, T7>
    {
        internal T1 Item1;
        internal T2 Item2;
        internal T3 Item3;
        internal T4 Item4;
        internal T5 Item5;
        internal T6 Item6;
        internal T7 Item7;
    }

    internal struct RuntimeAsyncSlots<T1, T2, T3, T4, T5, T6, T7, TRest>
    {
        internal T1 Item1;
        internal T2 Item2;
        internal T3 Item3;
        internal T4 Item4;
        internal T5 Item5;
        internal T6 Item6;
        internal T7 Item7;
        internal TRest Rest;
    }
#pragma warning restore CS0649

    // The contexts a lowered method puts back on its way out, as AsyncMethodBuilderCore.Start does around a state
    // machine's first step, and as upstream's CaptureContexts, RestoreContexts and RestoreContextsOnSuspension do around
    // a runtime-async method (on Thread's context fields, which NetWasm's single-threaded CoreLib does not have). Captured before the program changes any context, they are the defaults; in a program that
    // never does (AmbientContextChanges), capture and restore fold away.
    internal readonly struct RuntimeAsyncContexts
    {
        private readonly ExecutionContext _execution;
        private readonly int _flowControlToken;
        private readonly SynchronizationContext? _synchronization;

        internal RuntimeAsyncContexts(ExecutionContext execution, int flowControlToken, SynchronizationContext? synchronization)
        {
            _execution = execution;
            _flowControlToken = flowControlToken;
            _synchronization = synchronization;
        }

        internal static RuntimeAsyncContexts Capture() =>
            AmbientContextChanges.Made
                ? new(ExecutionContext.CaptureForRestore(out var token), token, SynchronizationContext.Current)
                : default;

        // Restore for contexts captured before the program changed any, which are the defaults, without a
        // RuntimeAsyncContexts: a struct holding references, returned or held whole, takes a value frame of its own.
        internal static void RestoreDefaults()
        {
            if (!AmbientContextChanges.Made) return;
            if (SynchronizationContext.Current is not null)
                SynchronizationContext.RestoreCurrent(null);
            ExecutionContext.Restore(ExecutionContext.Default, 0);
        }

        // A return, a throw, or the top of a chain: AsyncLocal values that change notify their handlers.
        internal void Restore()
        {
            if (!AmbientContextChanges.Made) return;
            RestoreSynchronization();
            ExecutionContext.Restore(_execution ?? ExecutionContext.Default, _flowControlToken);
        }

        // Unwinding a suspension: the context that runs code next is restored with notifications.
        internal void RestoreWithoutNotifications()
        {
            if (!AmbientContextChanges.Made) return;
            RestoreSynchronization();
            ExecutionContext.RestoreWithoutNotifications(_execution ?? ExecutionContext.Default, _flowControlToken);
        }

        private void RestoreSynchronization()
        {
            if (!ReferenceEquals(_synchronization, SynchronizationContext.Current))
                SynchronizationContext.RestoreCurrent(_synchronization);
        }
    }

    // How a lowered method was called: by a caller that wants a Task (Normal), by a lowered caller that awaits it at
    // once and takes its result from the slot (Direct), by the driver to continue after a suspension (Resumed), or as
    // Direct through a virtual or interface call, which also marks that it took the call (DirectVirtual).
    internal enum RuntimeAsyncCallMode
    {
        Normal,
        Direct,
        Resumed,
        DirectVirtual,
    }

    // No initializer: the field starts as default, so the class has no type initializer and an access needs no
    // initialization check (Roslyn emitted an empty one for `= default!`, which every access then checked).
    internal static class RuntimeAsyncResult<T>
    {
#pragma warning disable CS8618 // Starts as default; an initializer would bring back the type initializer.
        internal static T Value;
#pragma warning restore CS8618
    }

    // Moves a result slot's value out and back, for a result that waits for a posted continuation: other chains may use
    // the slot in between. Its own class, so a program that never posts one creates none.
    internal sealed class RuntimeAsyncResultStash<T> : IRuntimeAsyncResultStash
    {
        internal static readonly RuntimeAsyncResultStash<T> Instance = new();

        public object? Take()
        {
            object? value = RuntimeAsyncResult<T>.Value;
            RuntimeAsyncResult<T>.Value = default!;
            return value;
        }

        public void Put(object? value) => RuntimeAsyncResult<T>.Value = (T)value!;
    }

    internal interface IRuntimeAsyncResultStash
    {
        object? Take();

        void Put(object? value);
    }

    internal static class RuntimeAsync
    {
        internal static bool Suspending;
        // The lowered method a direct call is about to call, by its function pointer, until that method takes it; for a
        // virtual or interface call, AnyCallee.
        private static nint s_directCallee;
        private const nint AnyCallee = -1;
        // Set by a lowered method that took a virtual call's request, as the last thing it does before it returns; the
        // call's await reads it at once.
        private static bool s_tookDirect;
        private static RuntimeAsyncFrame? s_resuming;
        private static RuntimeAsyncFrame? s_head;
        private static RuntimeAsyncFrame? s_tail;
        // How the innermost suspended frame resumes; the driver reads it before TakeChain clears it.
        internal static Action<Action>? Subscription;
        private static Exception? s_fault;
        private static RuntimeAsyncContexts s_leaf;
        private static IRuntimeAsyncResultStash? s_stored;

        // A lowered caller calls a lowered method this way, right before the call. The request names the callee, so
        // nothing that runs between it and the callee's entry takes it: a static constructor the call runs first, which
        // may call other lowered methods for their tasks.
        internal static void CallDirect(nint callee) => s_directCallee = callee;

        // A request the callee never took, because the call threw before the callee began (a null receiver): a handler
        // of the calling method drops it before any of its code can call that method for its task.
        internal static void ClearDirect() => s_directCallee = 0;

        // A lowered caller's await of a virtual or interface call, right before the call: a lowered override takes it
        // directly; any other declines it as it starts (DeclineDirect, which the compiler gives each such override the
        // call reaches), so the lowered methods it calls for their tasks do not take it. Both stay calls: the compiler
        // finds the request and the declining start by them, after it has inlined small calls.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void CallDirectVirtual() => s_directCallee = AnyCallee;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void DeclineDirect()
        {
            if (s_directCallee == AnyCallee) s_directCallee = 0;
        }

        // Whether the program can change an ambient context: true, which the compiler folds to false once no reachable
        // code writes AmbientContextChanges.Made (ContextChangeFactProgramReducer). Unlike Made it holds before the first
        // change too, so a direct call of a lowered method skips the catch that would restore the caller's contexts only
        // where it is false. A call, so the compiler finds it after it has inlined small calls.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool ContextsMayChange() => true;

        // After a direct call: whether the callee suspended, so the caller suspends in turn.
        internal static bool IsSuspending() => Suspending;

        // The first thing a lowered method does, with its own function pointer: how it was called.
        internal static RuntimeAsyncCallMode TakeCallMode(nint self)
        {
            if (s_resuming is not null) return RuntimeAsyncCallMode.Resumed;
            if (s_directCallee != self) return RuntimeAsyncCallMode.Normal;
            s_directCallee = 0;
            return RuntimeAsyncCallMode.Direct;
        }

        // The same for a lowered virtual or interface method, which a virtual call's request may name (AnyCallee). Only
        // such methods take one, so only they pay for the test, and only they mark as they return that they took one
        // (the Virtual completions): static and other methods keep the smaller helpers, inlined where they are.
        internal static RuntimeAsyncCallMode TakeVirtualCallMode(nint self)
        {
            if (s_resuming is not null) return RuntimeAsyncCallMode.Resumed;
            var requested = s_directCallee;
            if (requested == self)
            {
                s_directCallee = 0;
                return RuntimeAsyncCallMode.Direct;
            }
            if (requested != AnyCallee) return RuntimeAsyncCallMode.Normal;
            s_directCallee = 0;
            return RuntimeAsyncCallMode.DirectVirtual;
        }

        // The frame a resumed activation continues from, as its method's own frame type.
        internal static TFrame TakeResumingFrame<TFrame>() where TFrame : RuntimeAsyncFrame
        {
            var frame = s_resuming!;
            s_resuming = null;
            return Unsafe.As<TFrame>(frame);
        }

        // A suspension of the frame: where it resumes, and the contexts it resumes under, taken at each suspension.
        private static void SuspendFrame(RuntimeAsyncFrame frame, int state)
        {
            frame.State = state;
            frame.Execution = AmbientContextChanges.Made ? ExecutionContext.Capture() : null;
            frame.Synchronization = AmbientContextChanges.Made ? OwnSynchronizationContext() : null;
        }

        // The current SynchronizationContext unless it is the default one, which continues anywhere: upstream's
        // CaptureContinuationContext test.
        private static SynchronizationContext? OwnSynchronizationContext() =>
            SynchronizationContext.Current is { } context && context.GetType() != typeof(SynchronizationContext)
                ? context
                : null;

        // A lowered method's entry: the contexts its exits restore unless it was resumed, which the lowering captures
        // only once the program has changed a context (AmbientContextChanges). The method holds them in three locals,
        // not a RuntimeAsyncContexts: its two references stay in the root frame it has anyway, where a struct holding
        // references took a value frame of its own, entered on every call and every resumption. The ExecutionContext's
        // flow-control token comes from CapturedToken, called right after CaptureExecution, with nothing between.
        private static int s_capturedToken;

        internal static ExecutionContext CaptureExecution() => ExecutionContext.CaptureForRestore(out s_capturedToken);

        internal static int CapturedToken() => s_capturedToken;

        internal static SynchronizationContext? CaptureSynchronization() => SynchronizationContext.Current;

        internal static void RestoreContexts(RuntimeAsyncCallMode mode, ExecutionContext execution, int token,
            SynchronizationContext? synchronization)
        {
            if (mode != RuntimeAsyncCallMode.Resumed) new RuntimeAsyncContexts(execution, token, synchronization).Restore();
        }

        // A suspension: a direct callee unwinds quietly into its caller; the top of the chain, whose Suspended left the
        // leaf's contexts current to subscribe under, restores its caller's with notifications, as upstream's thunk does.
        internal static void RestoreContextsOnSuspension(RuntimeAsyncCallMode mode, ExecutionContext execution, int token,
            SynchronizationContext? synchronization)
        {
            var contexts = new RuntimeAsyncContexts(execution, token, synchronization);
            if (mode is RuntimeAsyncCallMode.Direct or RuntimeAsyncCallMode.DirectVirtual) contexts.RestoreWithoutNotifications();
            else if (mode == RuntimeAsyncCallMode.Normal) contexts.Restore();
        }

        private static void PushFrame(RuntimeAsyncFrame frame)
        {
            if (s_head is null) s_head = s_tail = frame;
            else { s_tail!.Next = frame; s_tail = frame; }
        }

        // A suspension, after the frame holds what it keeps: where the frame resumes and under which contexts, the
        // subscription of the chain's resumption for a leaf await (the innermost frame subscribes it, under the contexts
        // it suspends with), and the frame pushed onto the chain. A suspension is the slow path of an await, and costs far
        // more than a call, so it is one call: inlined, it would only grow the method around every await that completes
        // at once. Each holds its awaiter itself rather than through SuspendOnAwaiter, which would copy the awaiter once
        // more on the way.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendOn(Task task, RuntimeAsyncFrame frame, int state)
        {
            SuspendFrame(frame, state);
            HoldTask(task);
            PushFrame(frame);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendOn<T>(Task<T> task, RuntimeAsyncFrame frame, int state)
        {
            SuspendFrame(frame, state);
            HoldTask(task);
            PushFrame(frame);
        }

        // The same for a leaf await of a task through ConfigureAwait(false), which the lowering takes as an await of
        // the task itself (RuntimeAsyncAwaits): it subscribes to continue wherever the task completes, as the
        // configured awaiter's UnsafeOnCompleted does.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendOnAnywhere(Task task, RuntimeAsyncFrame frame, int state)
        {
            SuspendFrame(frame, state);
            HoldTask(task, anywhere: true);
            PushFrame(frame);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendOnAnywhere<T>(Task<T> task, RuntimeAsyncFrame frame, int state)
        {
            SuspendFrame(frame, state);
            HoldTask(task, anywhere: true);
            PushFrame(frame);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendOn(ValueTask task, RuntimeAsyncFrame frame, int state)
        {
            SuspendFrame(frame, state);
            HoldValueTask(task.Subscribable, task.SubscriptionToken);
            PushFrame(frame);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendOn<T>(ValueTask<T> task, RuntimeAsyncFrame frame, int state)
        {
            SuspendFrame(frame, state);
            HoldValueTask<T>(task.Subscribable, task.SubscriptionToken);
            PushFrame(frame);
        }

        // A leaf await of a value task suspends through its address (RuntimeAsyncBodyBuilder): the subscription takes
        // the value task as its two values, so the await never copies the value task into a call.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SuspendOnAt(ref ValueTask task, RuntimeAsyncFrame frame, int state) =>
            SuspendOnValueTask(task.Subscribable, task.SubscriptionToken, frame, state, anywhere: false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SuspendOnAt<T>(ref ValueTask<T> task, RuntimeAsyncFrame frame, int state) =>
            SuspendOnValueTask<T>(task.Subscribable, task.SubscriptionToken, frame, state, anywhere: false);

        // Through ConfigureAwait(false), fused as a task's is (SuspendOnAnywhere).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SuspendOnAtAnywhere(ref ValueTask task, RuntimeAsyncFrame frame, int state) =>
            SuspendOnValueTask(task.Subscribable, task.SubscriptionToken, frame, state, anywhere: true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SuspendOnAtAnywhere<T>(ref ValueTask<T> task, RuntimeAsyncFrame frame, int state) =>
            SuspendOnValueTask<T>(task.Subscribable, task.SubscriptionToken, frame, state, anywhere: true);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SuspendOnValueTask(object? subscribable, short token, RuntimeAsyncFrame frame, int state,
            bool anywhere)
        {
            SuspendFrame(frame, state);
            HoldValueTask(subscribable, token, anywhere);
            PushFrame(frame);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SuspendOnValueTask<T>(object? subscribable, short token, RuntimeAsyncFrame frame, int state,
            bool anywhere)
        {
            SuspendFrame(frame, state);
            HoldValueTask<T>(subscribable, token, anywhere);
            PushFrame(frame);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendOn(ConfiguredTaskAwaitable task, RuntimeAsyncFrame frame, int state)
        {
            SuspendFrame(frame, state);
            HoldCritical(task.GetAwaiter());
            PushFrame(frame);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendOn<T>(ConfiguredTaskAwaitable<T> task, RuntimeAsyncFrame frame, int state)
        {
            SuspendFrame(frame, state);
            HoldCritical(task.GetAwaiter());
            PushFrame(frame);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendOn(ConfiguredValueTaskAwaitable task, RuntimeAsyncFrame frame, int state)
        {
            SuspendFrame(frame, state);
            HoldCritical(task.GetAwaiter());
            PushFrame(frame);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendOn<T>(ConfiguredValueTaskAwaitable<T> task, RuntimeAsyncFrame frame, int state)
        {
            SuspendFrame(frame, state);
            HoldCritical(task.GetAwaiter());
            PushFrame(frame);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendOnAwaiter<TAwaiter>(TAwaiter awaiter, RuntimeAsyncFrame frame, int state)
            where TAwaiter : ICriticalNotifyCompletion
        {
            SuspendFrame(frame, state);
            HoldCritical(awaiter);
            PushFrame(frame);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendOnNotify<TAwaiter>(TAwaiter awaiter, RuntimeAsyncFrame frame, int state)
            where TAwaiter : INotifyCompletion
        {
            SuspendFrame(frame, state);
            NotifySubscription<TAwaiter>.Awaiter = awaiter;
            Suspend(NotifySubscription<TAwaiter>.Subscribe ??= NotifySubscription<TAwaiter>.Run);
            PushFrame(frame);
        }

        // A direct callee suspended under this frame's await: the frame follows it onto the chain, with no subscription
        // of its own.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendDirect(RuntimeAsyncFrame frame, int state)
        {
            SuspendFrame(frame, state);
            PushFrame(frame);
        }

        // The same for an await of the callee's task through ConfigureAwait(false): the frame keeps no
        // SynchronizationContext, so it continues wherever its callee completes, as a configured awaiter would.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendDirectAnywhere(RuntimeAsyncFrame frame, int state)
        {
            frame.State = state;
            frame.Execution = AmbientContextChanges.Made ? ExecutionContext.Capture() : null;
            frame.Synchronization = null;
            PushFrame(frame);
        }

        // A task's suspension holds the task, which its awaiter's UnsafeOnCompleted subscribes on: a reference, where
        // holding the TaskAwaiter took a struct copy through its address to subscribe, a value frame on every suspension.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void HoldTask(Task task, bool anywhere = false)
        {
            TaskSubscription.Task = task;
            TaskSubscription.Anywhere = anywhere;
            Suspend(TaskSubscription.Subscribe ??= TaskSubscription.Run);
        }

        // A value task's suspension holds what it subscribes on (its IValueTaskSource or Task) and its token, as
        // upstream's HandleSuspended subscribes a ValueTaskSourceNotifier: references and a short, where holding the
        // ValueTaskAwaiter took a struct copy through its address.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void HoldValueTask(object? subscribable, short token, bool anywhere = false)
        {
            ValueTaskSubscription.Target = subscribable;
            ValueTaskSubscription.Token = token;
            ValueTaskSubscription.Anywhere = anywhere;
            Suspend(ValueTaskSubscription.Subscribe ??= ValueTaskSubscription.Run);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void HoldValueTask<T>(object? subscribable, short token, bool anywhere = false)
        {
            ValueTaskSubscription<T>.Target = subscribable;
            ValueTaskSubscription<T>.Token = token;
            ValueTaskSubscription<T>.Anywhere = anywhere;
            Suspend(ValueTaskSubscription<T>.Subscribe ??= ValueTaskSubscription<T>.Run);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void HoldCritical<TAwaiter>(TAwaiter awaiter) where TAwaiter : ICriticalNotifyCompletion
        {
            CriticalSubscription<TAwaiter>.Awaiter = awaiter;
            Suspend(CriticalSubscription<TAwaiter>.Subscribe ??= CriticalSubscription<TAwaiter>.Run);
        }

        // Upstream keeps the notifier of the suspension in progress in its RuntimeAsyncStackState (CriticalNotifier,
        // Notifier, or an awaiter inside the continuation with a per-type AwaiterContinuation function pointer) until
        // HandleSuspended subscribes the task's cached action to it. Here:
        // the awaiter of the suspension in progress, held until its chain subscribes, so a suspension makes no closure:
        // each awaiter type has one subscription delegate, made once. Suspensions are taken one at a time, in order, on
        // the one thread, so one awaiter per type is all a suspension in progress needs. The delegate is made at the type's
        // first suspension rather than by a field initializer, so the holders need no class constructor.
        private static class CriticalSubscription<TAwaiter> where TAwaiter : ICriticalNotifyCompletion
        {
            internal static TAwaiter? Awaiter;
            internal static Action<Action>? Subscribe;

            internal static void Run(Action resume)
            {
                var awaiter = Awaiter!;
                Awaiter = default;
                awaiter.UnsafeOnCompleted(resume);
            }
        }

        private static class TaskSubscription
        {
            internal static Task? Task;
            // Through ConfigureAwait(false): continues wherever the task completes.
            internal static bool Anywhere;
            internal static Action<Action>? Subscribe;

            // TaskAwaiter.UnsafeOnCompleted, or the configured awaiter's.
            internal static void Run(Action resume)
            {
                var task = Task!;
                Task = null;
                task.OnCompleted(resume, continueOnCapturedContext: !Anywhere, flowExecutionContext: false);
            }
        }

        // ValueTaskAwaiter.UnsafeOnCompleted: a source subscribes with the scheduling context and no ExecutionContext
        // flow, a task as its own awaiter does.
        private static class ValueTaskSubscription
        {
            internal static object? Target;
            internal static short Token;
            // Through ConfigureAwait(false): no scheduling context, as the configured awaiter subscribes.
            internal static bool Anywhere;
            internal static Action<Action>? Subscribe;

            internal static void Run(Action resume)
            {
                var target = Target;
                Target = null;
                if (target is IValueTaskSource source)
                {
                    source.OnCompleted(static state => ((Action)state!).Invoke(), resume, Token,
                        Anywhere ? ValueTaskSourceOnCompletedFlags.None : ValueTaskSourceOnCompletedFlags.UseSchedulingContext);
                }
                else if (target is Task task) task.OnCompleted(resume, continueOnCapturedContext: !Anywhere, flowExecutionContext: false);
                else PlatformServices.Scheduler.Post(resume);
            }
        }

        private static class ValueTaskSubscription<T>
        {
            internal static object? Target;
            internal static short Token;
            // Through ConfigureAwait(false): no scheduling context, as the configured awaiter subscribes.
            internal static bool Anywhere;
            internal static Action<Action>? Subscribe;

            internal static void Run(Action resume)
            {
                var target = Target;
                Target = null;
                if (target is IValueTaskSource<T> source)
                {
                    source.OnCompleted(static state => ((Action)state!).Invoke(), resume, Token,
                        Anywhere ? ValueTaskSourceOnCompletedFlags.None : ValueTaskSourceOnCompletedFlags.UseSchedulingContext);
                }
                else if (target is Task task) task.OnCompleted(resume, continueOnCapturedContext: !Anywhere, flowExecutionContext: false);
                else PlatformServices.Scheduler.Post(resume);
            }
        }

        private static class NotifySubscription<TAwaiter> where TAwaiter : INotifyCompletion
        {
            internal static TAwaiter? Awaiter;
            internal static Action<Action>? Subscribe;

            internal static void Run(Action resume)
            {
                var awaiter = Awaiter!;
                Awaiter = default;
                awaiter.OnCompleted(resume);
            }
        }

        private static void Suspend(Action<Action> subscribe)
        {
            Suspending = true;
            Subscription = subscribe;
            // Until the program changes a context the capture is the defaults, which s_leaf already holds: the flag
            // never clears, so every earlier suspension stored them too.
            if (AmbientContextChanges.Made)
                s_leaf = RuntimeAsyncContexts.Capture();
        }

        // AsyncHelpers.Await's shapes: whether the awaited operation has completed, and its result once it has.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompleted(Task task) => task.IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompleted<T>(Task<T> task) => task.IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompleted(ValueTask task) => task.IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompleted<T>(ValueTask<T> task) => task.IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompleted(ConfiguredTaskAwaitable task) => task.GetAwaiter().IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompleted<T>(ConfiguredTaskAwaitable<T> task) => task.GetAwaiter().IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompleted(ConfiguredValueTaskAwaitable task) => task.IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompleted<T>(ConfiguredValueTaskAwaitable<T> task) => task.IsCompleted;
        // The result of a completed task or value task, as its awaiter's GetResult takes it, without the awaiter: a
        // struct the lowered method would otherwise hold through its address where GetAwaiter stays a call (Oz), which
        // takes a value frame entered on every call and every resumption.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void GetResult(Task task) => task.GetVoidResult();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T GetResult<T>(Task<T> task) => task.GetResult();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void GetResult(ValueTask task) => task.GetResult();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T GetResult<T>(ValueTask<T> task) => task.GetResult();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void GetResult(ConfiguredTaskAwaitable task) => task.GetAwaiter().GetResult();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T GetResult<T>(ConfiguredTaskAwaitable<T> task) => task.GetAwaiter().GetResult();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void GetResult(ConfiguredValueTaskAwaitable task) => task.GetResult();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T GetResult<T>(ConfiguredValueTaskAwaitable<T> task) => task.GetResult();

        // The same through the task's address: a virtual call's await that keeps no request reads its task this way
        // (VirtualDirectCallProgramReducer), with no copy of a value task into a call.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompletedAt(ref Task task) => task.IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompletedAt<T>(ref Task<T> task) => task.IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompletedAt(ref ValueTask task) => task.IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompletedAt<T>(ref ValueTask<T> task) => task.IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void GetResultAt(ref Task task) => task.GetVoidResult();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T GetResultAt<T>(ref Task<T> task) => task.GetResult();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void GetResultAt(ref ValueTask task) => task.GetResult();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T GetResultAt<T>(ref ValueTask<T> task) => task.GetResult();
        // A leaf await of a value task, or a configured one, reads it this way too (RuntimeAsyncBodyBuilder.ReadAwaited).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompletedAt(ref ConfiguredValueTaskAwaitable task) => task.IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsCompletedAt<T>(ref ConfiguredValueTaskAwaitable<T> task) => task.IsCompleted;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void GetResultAt(ref ConfiguredValueTaskAwaitable task) => task.GetResult();
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T GetResultAt<T>(ref ConfiguredValueTaskAwaitable<T> task) => task.GetResult();

        // A result passed from a completed lowered method to the lowered caller that awaits it.
        internal static void StoreResult<T>(T value)
        {
            RuntimeAsyncResult<T>.Value = value;
            // Only a frame posted to a SynchronizationContext moves a result, so only a program that installs one keeps it.
            if (AmbientContextChanges.Made) s_stored = RuntimeAsyncResultStash<T>.Instance;
        }

        // The result a completed frame left for the frame that awaited it, moved out while that frame's continuation is
        // posted to its SynchronizationContext.
        internal static (IRuntimeAsyncResultStash? Slot, object? Value) TakeStoredResult()
        {
            var slot = s_stored;
            s_stored = null;
            return (slot, slot?.Take());
        }

        // The slot is cleared so it keeps no reference alive, which only a result that holds one needs; and a stash is
        // recorded only once a program has changed an ambient context, which it never undoes. Both tests fold, so a
        // value-type result in a program that changes no context is one load.
        internal static T TakeResult<T>()
        {
            ThrowIfFaulted();
            var value = RuntimeAsyncResult<T>.Value;
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) RuntimeAsyncResult<T>.Value = default!;
            if (AmbientContextChanges.Made) s_stored = null;
            return value;
        }

        internal static void TakeVoid() => ThrowIfFaulted();

        // What a lowered method returns when it completes: a completed task for a Normal call, nothing for the others,
        // whose caller takes the result from its slot.
        internal static Task<T>? Complete<T>(T value, RuntimeAsyncCallMode mode)
        {
            if (mode == RuntimeAsyncCallMode.Normal) return CompletedTask(value);
            StoreResult(value);
            return null;
        }

        // A non-virtual lowered method that cannot suspend and returns a reference passes its result to a direct caller as
        // the call's value, which a direct call otherwise leaves null: no slot is written, then read and cleared (two stores
        // to one address per level, which slowed a chain of such awaits more than its frame did). Such a method is never
        // resumed, so a call of it is Normal or Direct; its direct callers take the value with TakeReturned.
        // The result comes as an object, which a reference passes to with no conversion: a T would be boxed on the way to
        // Unsafe.As, and a box is a safepoint that would publish the result on the direct path.
        // Inlined at every level: a call would publish the reference it passes, entering the caller's root frame on the
        // path that otherwise needs none.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Task<T>? CompleteReturning<T>(object? value, RuntimeAsyncCallMode mode) where T : class?
        {
            if (mode == RuntimeAsyncCallMode.Normal) return CompletedTask(Unsafe.As<T>(value)!);
            return Unsafe.As<Task<T>>(value);
        }

        // A direct call's result from a method that completes through CompleteReturning, or the callee's exception.
        // Inlined at every level: a call would publish the reference it passes, entering the caller's root frame on the
        // path that otherwise needs none.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T TakeReturned<T>(Task<T>? returned) where T : class?
        {
            // ThrowIfFaulted's test in place, which a call of it would not be where it is not inlined: the result is held
            // across it, so only the throw may be a call.
            if (s_fault is { } error)
            {
                s_fault = null;
                ExceptionDispatchInfo.Throw(error);
            }
            return Unsafe.As<T>(returned)!;
        }

        internal static Task<T>? VirtualComplete<T>(T value, RuntimeAsyncCallMode mode)
        {
            if (mode == RuntimeAsyncCallMode.Normal) return CompletedTask(value);
            StoreResult(value);
            // A method that took a virtual call's request marks it as it returns: the call's await reads this at once.
            if (mode == RuntimeAsyncCallMode.DirectVirtual) s_tookDirect = true;
            return null;
        }

        // A Normal call's completed task, once per activation: a call rather than Task.FromResult's body in every lowered
        // method. A lowered caller's direct call takes its result from the slot without it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Task<T> CompletedTask<T>(T value) => Task.FromResult(value);

        internal static Task? Complete(RuntimeAsyncCallMode mode) =>
            mode == RuntimeAsyncCallMode.Normal ? Task.CompletedTask : null;

        internal static Task? VirtualComplete(RuntimeAsyncCallMode mode)
        {
            if (mode == RuntimeAsyncCallMode.Normal) return Task.CompletedTask;
            if (mode == RuntimeAsyncCallMode.DirectVirtual) s_tookDirect = true;
            return null;
        }

        internal static ValueTask<T> CompleteValue<T>(T value, RuntimeAsyncCallMode mode)
        {
            if (mode != RuntimeAsyncCallMode.Normal) StoreResult(value);
            return new ValueTask<T>(value);
        }

        internal static ValueTask<T> VirtualCompleteValue<T>(T value, RuntimeAsyncCallMode mode)
        {
            if (mode != RuntimeAsyncCallMode.Normal) StoreResult(value);
            if (mode == RuntimeAsyncCallMode.DirectVirtual) s_tookDirect = true;
            return new ValueTask<T>(value);
        }

        internal static ValueTask CompleteValue(RuntimeAsyncCallMode mode) => default;

        internal static ValueTask VirtualCompleteValue(RuntimeAsyncCallMode mode)
        {
            if (mode == RuntimeAsyncCallMode.DirectVirtual) s_tookDirect = true;
            return default;
        }

        // Upstream's CreateRuntimeAsyncTask and CreateRuntimeAsyncValueTask: what a lowered method returns once it has
        // pushed its frame. A Normal call returns the task its chain completes; the others return nothing,
        // since the caller suspends in turn.
        internal static Task<T>? Suspended<T>(RuntimeAsyncCallMode mode) =>
            mode == RuntimeAsyncCallMode.Normal ? CreateRuntimeAsyncTask<T>() : null;

        internal static Task<T>? VirtualSuspended<T>(RuntimeAsyncCallMode mode)
        {
            if (mode == RuntimeAsyncCallMode.Normal) return CreateRuntimeAsyncTask<T>();
            if (mode == RuntimeAsyncCallMode.DirectVirtual) s_tookDirect = true;
            return null;
        }

        internal static Task? Suspended(RuntimeAsyncCallMode mode) =>
            mode == RuntimeAsyncCallMode.Normal ? CreateRuntimeAsyncTask<VoidResult>() : null;

        internal static Task? VirtualSuspended(RuntimeAsyncCallMode mode)
        {
            if (mode == RuntimeAsyncCallMode.Normal) return CreateRuntimeAsyncTask<VoidResult>();
            if (mode == RuntimeAsyncCallMode.DirectVirtual) s_tookDirect = true;
            return null;
        }

        // We only come to these in the expensive case (already suspended), so ValueTask optimization here is not
        // relevant (upstream's comment on CreateRuntimeAsyncValueTask).
        internal static ValueTask<T> SuspendedValue<T>(RuntimeAsyncCallMode mode) =>
            mode == RuntimeAsyncCallMode.Normal ? new ValueTask<T>(CreateRuntimeAsyncTask<T>()) : default;

        internal static ValueTask<T> VirtualSuspendedValue<T>(RuntimeAsyncCallMode mode)
        {
            if (mode == RuntimeAsyncCallMode.Normal) return new ValueTask<T>(CreateRuntimeAsyncTask<T>());
            if (mode == RuntimeAsyncCallMode.DirectVirtual) s_tookDirect = true;
            return default;
        }

        internal static ValueTask SuspendedValue(RuntimeAsyncCallMode mode) =>
            mode == RuntimeAsyncCallMode.Normal ? new ValueTask(CreateRuntimeAsyncTask<VoidResult>()) : default;

        internal static ValueTask VirtualSuspendedValue(RuntimeAsyncCallMode mode)
        {
            if (mode == RuntimeAsyncCallMode.Normal) return new ValueTask(CreateRuntimeAsyncTask<VoidResult>());
            if (mode == RuntimeAsyncCallMode.DirectVirtual) s_tookDirect = true;
            return default;
        }

        // After a virtual or interface call that its await asked to take directly: whether the await is done, its result
        // in the slot. An override that took the call is done unless it suspended; any other returned its task, done once
        // it has completed, its result (or exception) taken now. The await's helpers take its task by address, so no call
        // copies a value task, and stay calls: the compiler finds them to make an await that keeps no request a plain
        // await of its task (VirtualDirectCallProgramReducer).
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool VirtualDone(ref Task task)
        {
            if (TakeTook()) return !Suspending;
            if (!task.IsCompleted) return false;
            task.GetAwaiter().GetResult();
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool VirtualDone<T>(ref Task<T> task)
        {
            if (TakeTook()) return !Suspending;
            if (!task.IsCompleted) return false;
            StoreResult(task.GetAwaiter().GetResult());
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool VirtualDone(ref ValueTask task)
        {
            if (TakeTook()) return !Suspending;
            if (!task.IsCompleted) return false;
            task.GetAwaiter().GetResult();
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool VirtualDone<T>(ref ValueTask<T> task)
        {
            if (TakeTook()) return !Suspending;
            if (!task.IsCompleted) return false;
            StoreResult(task.GetAwaiter().GetResult());
            return true;
        }

        private static bool TakeTook()
        {
            var took = s_tookDirect;
            s_tookDirect = false;
            return took;
        }

        // The result of a virtual call's await, from the slot: TakeResult and TakeVoid, as calls of their own.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static T VirtualResult<T>() => TakeResult<T>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void VirtualVoid() => ThrowIfFaulted();

        // A virtual call's await that is not done suspends: under the override's frames when it took the call, else on
        // its task, through ConfigureAwait(false) for the Anywhere forms.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendVirtual(Task task, RuntimeAsyncFrame frame, int state)
        {
            if (Suspending) SuspendDirect(frame, state);
            else SuspendOn(task, frame, state);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendVirtual<T>(Task<T> task, RuntimeAsyncFrame frame, int state)
        {
            if (Suspending) SuspendDirect(frame, state);
            else SuspendOn(task, frame, state);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendVirtual(ValueTask task, RuntimeAsyncFrame frame, int state)
        {
            if (Suspending) SuspendDirect(frame, state);
            else SuspendOn(task, frame, state);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendVirtual<T>(ValueTask<T> task, RuntimeAsyncFrame frame, int state)
        {
            if (Suspending) SuspendDirect(frame, state);
            else SuspendOn(task, frame, state);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendVirtualAnywhere(Task task, RuntimeAsyncFrame frame, int state)
        {
            if (Suspending) SuspendDirectAnywhere(frame, state);
            else SuspendOn(task.ConfigureAwait(false), frame, state);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendVirtualAnywhere<T>(Task<T> task, RuntimeAsyncFrame frame, int state)
        {
            if (Suspending) SuspendDirectAnywhere(frame, state);
            else SuspendOn(task.ConfigureAwait(false), frame, state);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendVirtualAnywhere(ValueTask task, RuntimeAsyncFrame frame, int state)
        {
            if (Suspending) SuspendDirectAnywhere(frame, state);
            else SuspendOn(task.ConfigureAwait(false), frame, state);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SuspendVirtualAnywhere<T>(ValueTask<T> task, RuntimeAsyncFrame frame, int state)
        {
            if (Suspending) SuspendDirectAnywhere(frame, state);
            else SuspendOn(task.ConfigureAwait(false), frame, state);
        }

        // Resumed after a virtual call's await suspended: on the task of an override that did not take the call, whose
        // result (or exception) goes to the slot now; an override that took it returned no task and left its result
        // there itself.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void VirtualResumed(ref Task task) => task?.GetAwaiter().GetResult();

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void VirtualResumed<T>(ref Task<T> task)
        {
            if (task is not null) StoreResult(task.GetAwaiter().GetResult());
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void VirtualResumed(ref ValueTask task)
        {
            if (task.Subscribable is not null) task.GetAwaiter().GetResult();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void VirtualResumed<T>(ref ValueTask<T> task)
        {
            if (task.Subscribable is not null) StoreResult(task.GetAwaiter().GetResult());
        }

        // Upstream's CreateRuntimeAsyncTask and FinalizeRuntimeAsyncTask: the task, its chain subscribed to what the chain
        // awaits. Once per activation a Normal call suspends, so it stays a call: inlined, the driver, its task and the
        // subscription would grow every lowered method's resumed path.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Task<T> CreateRuntimeAsyncTask<T>()
        {
            RuntimeAsyncDriver<T> driver = new();
            driver.Take();
            return driver.Task;
        }

        // Upstream's TaskFromException and ValueTaskFromException: what a lowered method returns when it throws before it
        // first suspends, for a Normal call (the others rethrow): a promise task ended as the builder ends one. A throw is
        // the cold path of every lowered method, so these stay calls rather than grow each method's handler.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Task<T> Faulted<T>(Exception error)
        {
            Task<T> task = new();
            bool successfullySet = TrySetExceptionAsBuilder(task, error);
            Debug.Assert(successfullySet);
            return task;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Task Faulted(Exception error)
        {
            Task task = new();
            bool successfullySet = TrySetExceptionAsBuilder(task, error);
            Debug.Assert(successfullySet);
            return task;
        }

        // We only come to these methods in the expensive case (exception), so ValueTask optimization here is not
        // relevant (upstream's comment on ValueTaskFromException).
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static ValueTask<T> FaultedValue<T>(Exception error) => new(Faulted<T>(error));

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static ValueTask FaultedValue(Exception error) => new(Faulted(error));

        // Tail of AsyncTaskMethodBuilderT.SetException, as upstream's TaskFromException and DispatchContinuations end a
        // task: an OperationCanceledException cancels it, and the task keeps that exception (awaiting the task rethrows
        // it; Result throws a TaskCanceledException in an AggregateException); anything else faults it. Upstream passes
        // oce.CancellationToken beside the exception; NetWasm's TrySetCanceled takes the token from the exception.
        internal static bool TrySetExceptionAsBuilder(Task task, Exception error) =>
            error is OperationCanceledException ? task.TrySetCanceled(error) : task.TrySetException(error);

        internal static int State(RuntimeAsyncFrame frame) => frame.State;

        // The frames suspended since the last capture, innermost first, without a tuple with the subscription: a struct
        // holding references, returned by value, is built in a value frame and copied out. The leaf's contexts become
        // current for the subscription, as upstream's suspension does; whoever unwinds to its caller next restores
        // that caller's.
        internal static RuntimeAsyncFrame? TakeChain()
        {
            var frames = s_head;
            Reset();
            if (AmbientContextChanges.Made)
            {
                var leaf = s_leaf;
                s_leaf = default;
                leaf.RestoreWithoutNotifications();
            }
            return frames;
        }

        internal static void Resume(RuntimeAsyncFrame frame)
        {
            s_resuming = frame;
            frame.Resume();
        }

        internal static void Fault(Exception error)
        {
            Reset();
            s_fault = error;
        }

        internal static void AppendToSuspension(RuntimeAsyncFrame? outer) => s_tail!.Next = outer;

        private static void Reset()
        {
            Suspending = false;
            s_head = s_tail = null;
            Subscription = null;
        }

        private static void ThrowIfFaulted()
        {
            if (s_fault is { } error)
            {
                s_fault = null;
                ExceptionDispatchInfo.Throw(error);
            }
        }

        internal static Exception? TakeFault()
        {
            var error = s_fault;
            s_fault = null;
            return error;
        }

        internal readonly struct VoidResult;
    }

    // Upstream's RuntimeAsyncTask<T> and its DispatchContinuations, with the dispatcher kept apart from the task:
    // upstream's task is its own dispatcher, one object; NetWasm's driver completes a plain promise Task<T> instead, as
    // a Task<T> subclass in the program slowed awaits everywhere (value-task-source 1.08 at O3, a chain that never
    // suspends 1.09 at Oz), and costs 2 to 5 KB at Oz. Completes the task a Normal call of a suspended lowered method
    // returned: once the awaited operation completes it
    // resumes the frames, innermost first, handing each one's result (or exception) to the next through the result
    // slot; when a resumed frame suspends again, it subscribes again to what that frame awaits. Each frame runs under
    // the ExecutionContext it suspended with, and the dispatcher's contexts come back when the frames stop. Only the
    // completion depends on the result type, so the frame loop is compiled once, not per result type.
    internal abstract class RuntimeAsyncDriver
    {
        private RuntimeAsyncFrame? _frames;
        private Action? _run;

        internal void Take()
        {
            var subscribe = RuntimeAsync.Subscription;
            _frames = RuntimeAsync.TakeChain();
            _run ??= Run;
            subscribe!(_run);
        }

        private protected abstract void Complete();

        // The innermost frame's awaiter has already chosen where it continues.
        private void Run()
        {
            var frame = _frames;
            _frames = null;
            RunFrom(frame, firstChecksContext: false);
        }

        // The dispatcher's contexts come back when the frames stop. Until the program changes a context they are the
        // defaults and nothing is restored, so the frames run outside a protected block: each resumption would otherwise
        // enter one more exception frame for nothing. A frame may make the first change; the defaults come back then.
        private void RunFrom(RuntimeAsyncFrame? frame, bool firstChecksContext)
        {
            if (!AmbientContextChanges.Made)
            {
                RunFrames(frame, firstChecksContext);
                RuntimeAsyncContexts.RestoreDefaults();
                return;
            }
            var dispatcher = RuntimeAsyncContexts.Capture();
            try
            {
                RunFrames(frame, firstChecksContext);
            }
            finally
            {
                dispatcher.Restore();
            }
        }

        private void RunFrames(RuntimeAsyncFrame? frame, bool firstChecksContext)
        {
            var checksContext = firstChecksContext;
            while (frame is not null)
            {
                // A frame that awaited its callee on a SynchronizationContext of its own continues there, as an
                // await with ConfigureAwait(true) does: posted, with the callee's result or exception moved along.
                // The flag is tested on its own, so the test folds where nothing changes a context.
                if (AmbientContextChanges.Made)
                {
                    if (checksContext && frame.Synchronization is { } context &&
                        !ReferenceEquals(context, SynchronizationContext.Current))
                    {
                        Post(frame, context);
                        return;
                    }
                    if (frame.Execution is { } execution) ExecutionContext.Restore(execution);
                }
                checksContext = true;
                var outer = frame.Next;
                frame.Next = null;
                // A frame that throws records its exception for the frame that awaited it (RuntimeAsync.Fault) and
                // returns, so resuming it needs no protected block here: one exception frame less per resumption.
                RuntimeAsync.Resume(frame);
                if (RuntimeAsync.Suspending)
                {
                    RuntimeAsync.AppendToSuspension(outer);
                    Take();
                    return;
                }
                frame = outer;
            }
            Complete();
        }

        private void Post(RuntimeAsyncFrame frame, SynchronizationContext context)
        {
            var fault = RuntimeAsync.TakeFault();
            var (slot, value) = RuntimeAsync.TakeStoredResult();
            context.Post(_ =>
            {
                if (fault is not null) RuntimeAsync.Fault(fault);
                slot?.Put(value);
                RunFrom(frame, firstChecksContext: false);
            }, null);
        }
    }

    internal sealed class RuntimeAsyncDriver<T> : RuntimeAsyncDriver
    {
        internal readonly Task<T> Task = new();

        // The end of upstream's DispatchContinuations.
        private protected override void Complete()
        {
            bool successfullySet = RuntimeAsync.TakeFault() is { } ex
                // Tail of AsyncTaskMethodBuilderT.SetException
                ? RuntimeAsync.TrySetExceptionAsBuilder(Task, ex)
                : Task.TrySetResult(typeof(T) == typeof(RuntimeAsync.VoidResult) ? default! : RuntimeAsync.TakeResult<T>());
            if (!successfullySet)
            {
                ThrowHelper.ThrowInvalidOperationException(ExceptionResource.TaskT_TransitionToFinal_AlreadyCompleted);
            }
        }
    }
}
