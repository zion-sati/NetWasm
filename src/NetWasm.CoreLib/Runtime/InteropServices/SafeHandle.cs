// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Diagnostics;
using System.Runtime.ConstrainedExecution;
using System.Threading;

namespace System.Runtime.InteropServices
{
    // NetWasm does not support thread aborts or last-P/Invoke-error preservation.
    // The upstream reference-counted ownership and disposal state machine is retained.

    /// <summary>Represents a wrapper class for operating system handles.</summary>
    public abstract partial class SafeHandle : CriticalFinalizerObject, IDisposable
    {
        /// <summary>Specifies the handle to be wrapped.</summary>
        protected IntPtr handle;

        /// <summary>Combined reference count and closed/disposed flags.</summary>
        private volatile int _state;

        /// <summary>Whether this instance owns the underlying handle.</summary>
        private readonly bool _ownsHandle;

        /// <summary>Whether construction completed.</summary>
        private readonly bool _fullyInitialized;

        private static class StateBits
        {
            public const int Closed = 0b01;
            public const int Disposed = 0b10;
            public const int RefCount = unchecked(~0b11);
            public const int RefCountOne = 1 << 2;
        }

        protected SafeHandle(IntPtr invalidHandleValue, bool ownsHandle)
        {
            handle = invalidHandleValue;
            _state = StateBits.RefCountOne;
            _ownsHandle = ownsHandle;

            if (!ownsHandle)
            {
                GC.SuppressFinalize(this);
            }

            Volatile.Write(ref _fullyInitialized, true);
        }

        ~SafeHandle()
        {
            if (_fullyInitialized)
            {
                Dispose(disposing: false);
            }
        }

        internal bool OwnsHandle => _ownsHandle;

        protected internal void SetHandle(IntPtr handle) => this.handle = handle;

        public IntPtr DangerousGetHandle() => handle;

        public bool IsClosed => (_state & StateBits.Closed) == StateBits.Closed;

        public abstract bool IsInvalid { get; }

        public void Close() => Dispose();

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            Debug.Assert(_fullyInitialized);
            InternalRelease(disposeOrFinalizeOperation: true);
        }

        public void SetHandleAsInvalid()
        {
            Debug.Assert(_fullyInitialized);
            Interlocked.Or(ref _state, StateBits.Closed);
            GC.SuppressFinalize(this);
        }

        protected abstract bool ReleaseHandle();

        public void DangerousAddRef(ref bool success)
        {
            Debug.Assert(_fullyInitialized);

            int oldState;
            int newState;
            do
            {
                oldState = _state;
                ObjectDisposedException.ThrowIf((oldState & StateBits.Closed) != 0, this);
                newState = oldState + StateBits.RefCountOne;
            } while (Interlocked.CompareExchange(ref _state, newState, oldState) != oldState);

            success = true;
        }

        internal void DangerousAddRef()
        {
            var success = false;
            DangerousAddRef(ref success);
        }

        public void DangerousRelease() => InternalRelease(disposeOrFinalizeOperation: false);

        private void InternalRelease(bool disposeOrFinalizeOperation)
        {
            Debug.Assert(_fullyInitialized || disposeOrFinalizeOperation);

            bool performRelease;
            int oldState;
            int newState;
            do
            {
                oldState = _state;

                if (disposeOrFinalizeOperation && (oldState & StateBits.Disposed) != 0)
                {
                    return;
                }

                ObjectDisposedException.ThrowIf((oldState & StateBits.RefCount) == 0, this);

                performRelease =
                    (oldState & (StateBits.RefCount | StateBits.Closed)) == StateBits.RefCountOne &&
                    _ownsHandle &&
                    !IsInvalid;

                newState = oldState - StateBits.RefCountOne;
                if ((oldState & StateBits.RefCount) == StateBits.RefCountOne)
                {
                    newState |= StateBits.Closed;
                }
                if (disposeOrFinalizeOperation)
                {
                    newState |= StateBits.Disposed;
                }
            } while (Interlocked.CompareExchange(ref _state, newState, oldState) != oldState);

            if (performRelease)
            {
                ReleaseHandle();
            }
        }
    }
}
