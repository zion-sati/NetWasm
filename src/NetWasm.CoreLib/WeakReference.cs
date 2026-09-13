// Portions derived from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System
{
    [Serializable]
    [TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class WeakReference
    {
        // NetWasm stores the runtime weak-handle index separately from the
        // resurrection policy represented by the upstream tagged handle.
        private int _handle;
        private bool _trackResurrection;

        // Creates a new WeakReference that keeps track of target.
        // Assumes a Short Weak Reference (ie TrackResurrection is false.)
        public WeakReference(object? target)
            : this(target, false)
        {
        }

        public WeakReference(object? target, bool trackResurrection)
        {
            Create(target, trackResurrection);
        }

        // Returns a boolean indicating whether or not we're tracking objects
        // until they're collected (true) or just until they're finalized
        // (false).
        public virtual bool TrackResurrection => _trackResurrection;

        private void Create(object? target, bool trackResurrection)
        {
            _handle = WeakReferenceRuntime.Create(target, trackResurrection);
            _trackResurrection = trackResurrection;
        }

        // Determines whether or not this instance of WeakReference still
        // refers to an object that has not been collected.
        public virtual bool IsAlive => WeakReferenceRuntime.Get(_handle) is not null;

        // Gets or sets the object stored in the handle.
        public virtual object? Target
        {
            get => WeakReferenceRuntime.Get(_handle);
            set
            {
                // A derived type can expose this path while its base finalizer
                // is running, just as the upstream implementation permits.
                if (_handle == 0)
                {
                    throw new InvalidOperationException("The weak-reference handle is not initialized.");
                }

                WeakReferenceRuntime.Set(_handle, value);
            }
        }

        // Free all system resources associated with this reference.
        ~WeakReference()
        {
            // Unlike WeakReference<T>, the non-generic type can be derived and
            // therefore is finalized through an ordinary finalizer.
            Debug.Assert(this.GetType() != typeof(WeakReference));

            var handle = _handle;
            if (handle != 0)
            {
                WeakReferenceRuntime.Release(handle);
                _handle = 0;
            }
        }
    }
}
