// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// A CoreLib member the runtime async lowering calls.
internal abstract record RuntimeAsyncMember
{
    private RuntimeAsyncMember()
    {
    }

    public static RuntimeAsyncMember Helper(string name) => new Named(name, []);

    // System.Runtime.CompilerServices.RuntimeAsync's method of this name, instantiated with these method arguments.
    public sealed record Named(string Name, ImmutableArray<CliTypeIdentity> MethodArguments) : RuntimeAsyncMember;

    // RuntimeAsync's overload of this name for an awaited shape (Task, Task<T>, ValueTask, ... or an awaiter type).
    public sealed record ForAwaited(string Name, CliTypeIdentity Awaited) : RuntimeAsyncMember;

    // The only constructor of a constructed type: a delegate's (object, IntPtr), or a frame's (its method's delegate).
    public sealed record Constructor(CliTypeIdentity Constructed) : RuntimeAsyncMember;

    // System.Runtime.CompilerServices.AsyncHelpers' method of this name, instantiated with these method arguments
    // (UnsafeAwaitAwaiter and AwaitAwaiter over an awaiter type).
    public sealed record AsyncHelpersNamed(string Name, ImmutableArray<CliTypeIdentity> MethodArguments) : RuntimeAsyncMember;

    // AsyncHelpers' overload of this name for an awaited shape (Await of a Task, a ValueTask or a configured one).
    public sealed record AsyncHelpersForAwaited(string Name, CliTypeIdentity Awaited) : RuntimeAsyncMember;

    // A task's or value task's own ConfigureAwait(bool continueOnCapturedContext).
    public sealed record ConfigureAwait(CliTypeIdentity Awaitable) : RuntimeAsyncMember;
}
