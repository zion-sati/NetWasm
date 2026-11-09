// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// Preserve managed handle/disposal contracts; host waiting remains unavailable.
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32.SafeHandles;
namespace System.Threading
{
    public abstract class WaitHandle : MarshalByRefObject, IDisposable
    {
        protected static readonly IntPtr InvalidHandle = new IntPtr(-1);
        public const int WaitTimeout = 0x102;
        private SafeWaitHandle? _waitHandle;
        protected WaitHandle() { }

        [Obsolete("WaitHandle.Handle has been deprecated. Use the SafeWaitHandle property instead.")]
        public virtual IntPtr Handle
        {
            get => _waitHandle == null ? InvalidHandle : _waitHandle.DangerousGetHandle();
            set
            {
                if (value == InvalidHandle)
                {
                    // This line leaks a handle.  However, it's currently
                    // not perfectly clear what the right behavior is here
                    // anyways.  This preserves Everett behavior.  We should
                    // ideally do these things:
                    // *) Expose a settable SafeHandle property on WaitHandle.
                    // *) Expose a settable OwnsHandle property on SafeHandle.
                    if (_waitHandle != null)
                    {
                        _waitHandle.SetHandleAsInvalid();
                        _waitHandle = null;
                    }
                }
                else
                {
                    _waitHandle = new SafeWaitHandle(value, true);
                }
            }
        }

        [AllowNull]
        public SafeWaitHandle SafeWaitHandle
        {
            get => _waitHandle ??= new SafeWaitHandle(InvalidHandle, false);
            set => _waitHandle = value;
        }

        internal static int ToTimeoutMilliseconds(TimeSpan timeout)
        {
            long timeoutMilliseconds = (long)timeout.TotalMilliseconds;
            ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, -1, nameof(timeout));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(timeoutMilliseconds, int.MaxValue, nameof(timeout));
            return (int)timeoutMilliseconds;
        }

        public virtual void Close() => Dispose();

        protected virtual void Dispose(bool explicitDisposing)
        {
            _waitHandle?.Close();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual bool WaitOne(int millisecondsTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
            throw new PlatformNotSupportedException("WaitHandle.WaitOne host waiting is not available on this NetWasm platform.");
        }
        public virtual bool WaitOne() =>
            throw new PlatformNotSupportedException("WaitHandle.WaitOne host waiting is not available on this NetWasm platform.");
        public virtual bool WaitOne(TimeSpan timeout)
        {
            _ = ToTimeoutMilliseconds(timeout);
            throw new PlatformNotSupportedException("WaitHandle.WaitOne host waiting is not available on this NetWasm platform.");
        }
        public virtual bool WaitOne(int millisecondsTimeout, bool exitContext) => WaitOne(millisecondsTimeout);
        public virtual bool WaitOne(TimeSpan timeout, bool exitContext)
        {
            _ = ToTimeoutMilliseconds(timeout);
            throw new PlatformNotSupportedException("WaitHandle.WaitOne host waiting is not available on this NetWasm platform.");
        }

        public static System.Boolean SignalAndWait(System.Threading.WaitHandle @toSignal, System.Threading.WaitHandle @toWaitOn) =>
            throw new PlatformNotSupportedException("WaitHandle.SignalAndWait host waiting is not available on this NetWasm platform.");

        public static System.Boolean SignalAndWait(System.Threading.WaitHandle @toSignal, System.Threading.WaitHandle @toWaitOn, System.Int32 @millisecondsTimeout, System.Boolean @exitContext) =>
            throw new PlatformNotSupportedException("WaitHandle.SignalAndWait host waiting is not available on this NetWasm platform.");

        public static System.Boolean SignalAndWait(System.Threading.WaitHandle @toSignal, System.Threading.WaitHandle @toWaitOn, System.TimeSpan @timeout, System.Boolean @exitContext) =>
            throw new PlatformNotSupportedException("WaitHandle.SignalAndWait host waiting is not available on this NetWasm platform.");

        public static System.Boolean WaitAll(System.Threading.WaitHandle[] @waitHandles) =>
            throw new PlatformNotSupportedException("WaitHandle.WaitAll host waiting is not available on this NetWasm platform.");

        public static System.Boolean WaitAll(System.Threading.WaitHandle[] @waitHandles, System.Int32 @millisecondsTimeout) =>
            throw new PlatformNotSupportedException("WaitHandle.WaitAll host waiting is not available on this NetWasm platform.");

        public static System.Boolean WaitAll(System.Threading.WaitHandle[] @waitHandles, System.Int32 @millisecondsTimeout, System.Boolean @exitContext) =>
            throw new PlatformNotSupportedException("WaitHandle.WaitAll host waiting is not available on this NetWasm platform.");

        public static System.Boolean WaitAll(System.Threading.WaitHandle[] @waitHandles, System.TimeSpan @timeout) =>
            throw new PlatformNotSupportedException("WaitHandle.WaitAll host waiting is not available on this NetWasm platform.");

        public static System.Boolean WaitAll(System.Threading.WaitHandle[] @waitHandles, System.TimeSpan @timeout, System.Boolean @exitContext) =>
            throw new PlatformNotSupportedException("WaitHandle.WaitAll host waiting is not available on this NetWasm platform.");

        public static System.Int32 WaitAny(System.Threading.WaitHandle[] @waitHandles) =>
            throw new PlatformNotSupportedException("WaitHandle.WaitAny host waiting is not available on this NetWasm platform.");

        public static System.Int32 WaitAny(System.Threading.WaitHandle[] @waitHandles, System.Int32 @millisecondsTimeout) =>
            throw new PlatformNotSupportedException("WaitHandle.WaitAny host waiting is not available on this NetWasm platform.");

        public static System.Int32 WaitAny(System.Threading.WaitHandle[] @waitHandles, System.Int32 @millisecondsTimeout, System.Boolean @exitContext) =>
            throw new PlatformNotSupportedException("WaitHandle.WaitAny host waiting is not available on this NetWasm platform.");

        public static System.Int32 WaitAny(System.Threading.WaitHandle[] @waitHandles, System.TimeSpan @timeout) =>
            throw new PlatformNotSupportedException("WaitHandle.WaitAny host waiting is not available on this NetWasm platform.");

        public static System.Int32 WaitAny(System.Threading.WaitHandle[] @waitHandles, System.TimeSpan @timeout, System.Boolean @exitContext) =>
            throw new PlatformNotSupportedException("WaitHandle.WaitAny host waiting is not available on this NetWasm platform.");

    }
}
