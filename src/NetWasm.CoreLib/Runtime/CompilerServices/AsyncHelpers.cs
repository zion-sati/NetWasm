// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copied from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncHelpers.cs, the surface of a platform
// whose runtime does not suspend (the #else branch).
// NetWasm: Roslyn's runtime-async=on emits these awaits in methods marked MethodImplOptions.Async when the corlib defines
// them. NetWasm's compiler lowers those methods itself (or names one it cannot lower), so these bodies run only when
// something calls an await outside a runtime-async method, and then throw as upstream's do. HandleAsyncEntryPoint is
// left out: Roslyn would route every async Main through it, and NetWasm's command adapter runs async Main itself.

using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static partial class AsyncHelpers
    {
        public static void UnsafeAwaitAwaiter<TAwaiter>(TAwaiter awaiter) where TAwaiter : ICriticalNotifyCompletion { throw new PlatformNotSupportedException("Runtime Async is not supported on this platform."); }
        public static void AwaitAwaiter<TAwaiter>(TAwaiter awaiter) where TAwaiter : INotifyCompletion { throw new PlatformNotSupportedException("Runtime Async is not supported on this platform."); }
        public static void Await(System.Threading.Tasks.Task task) { throw new PlatformNotSupportedException("Runtime Async is not supported on this platform."); }
        public static T Await<T>(System.Threading.Tasks.Task<T> task) { throw new PlatformNotSupportedException("Runtime Async is not supported on this platform."); }
        public static void Await(System.Threading.Tasks.ValueTask task) { throw new PlatformNotSupportedException("Runtime Async is not supported on this platform."); }
        public static T Await<T>(System.Threading.Tasks.ValueTask<T> task) { throw new PlatformNotSupportedException("Runtime Async is not supported on this platform."); }
        public static void Await(System.Runtime.CompilerServices.ConfiguredTaskAwaitable configuredAwaitable) { throw new PlatformNotSupportedException("Runtime Async is not supported on this platform."); }
        public static void Await(System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable configuredAwaitable) { throw new PlatformNotSupportedException("Runtime Async is not supported on this platform."); }
        public static T Await<T>(System.Runtime.CompilerServices.ConfiguredTaskAwaitable<T> configuredAwaitable) { throw new PlatformNotSupportedException("Runtime Async is not supported on this platform."); }
        public static T Await<T>(System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<T> configuredAwaitable) { throw new PlatformNotSupportedException("Runtime Async is not supported on this platform."); }
    }
}
