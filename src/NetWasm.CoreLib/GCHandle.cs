// Portions derived from dotnet/runtime System.Private.CoreLib GCHandle.cs at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    /// <summary>
    /// Represents an opaque handle to a managed object.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct GCHandle : IEquatable<GCHandle>
    {
        // The runtime encodes the handle kind in the low two bits.
        private IntPtr _handle;

        private GCHandle(object? value, GCHandleType type)
        {
            if ((uint)type > (uint)GCHandleType.Pinned)
            {
                throw new ArgumentOutOfRangeException(nameof(type));
            }

            // The NetWasm pinning runtime supports pinning any managed object,
            // so no separate Marshal.IsPinnable dependency is required here.
            _handle = GCHandleRuntime.Create(value, (int)type);
        }

        private GCHandle(IntPtr handle) => _handle = handle;

        /// <summary>Creates a normal GC handle for an object.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GCHandle Alloc(object? value) => new(value, GCHandleType.Normal);

        /// <summary>Creates a GC handle of the specified kind for an object.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GCHandle Alloc(object? value, GCHandleType type) => new(value, type);

        /// <summary>Frees this GC handle.</summary>
        public void Free()
        {
            var handle = _handle;
            _handle = IntPtr.Zero;
            ThrowIfInvalid(handle);
            GCHandleRuntime.Release(handle.ToInt32());
        }

        /// <summary>Gets or sets the object referenced by this handle.</summary>
        public object? Target
        {
            readonly get
            {
                var handle = _handle;
                ThrowIfInvalid(handle);
                return GCHandleRuntime.Get(handle.ToInt32());
            }
            set
            {
                var handle = _handle;
                ThrowIfInvalid(handle);
                GCHandleRuntime.Set(handle.ToInt32(), value);
            }
        }

        /// <summary>Gets the address of the data in a pinned handle.</summary>
        public readonly IntPtr AddrOfPinnedObject()
        {
            var handle = _handle;
            ThrowIfInvalid(handle);
            if (!IsPinned(handle))
            {
                throw new InvalidOperationException("The GC handle is not pinned.");
            }

            return GCHandleRuntime.Address(handle.ToInt32());
        }

        /// <summary>Determines whether this handle has been allocated.</summary>
        public readonly bool IsAllocated => _handle != IntPtr.Zero;

        public static explicit operator GCHandle(IntPtr value) => FromIntPtr(value);

        public static GCHandle FromIntPtr(IntPtr value)
        {
            ThrowIfInvalid(value);
            return new GCHandle(value);
        }

        public static explicit operator IntPtr(GCHandle value) => ToIntPtr(value);

        public static IntPtr ToIntPtr(GCHandle value) => value._handle;

        public override readonly int GetHashCode() => _handle.GetHashCode();

        public override readonly bool Equals([NotNullWhen(true)] object? obj) =>
            obj is GCHandle other && Equals(other);

        public readonly bool Equals(GCHandle other) => _handle == other._handle;

        public static bool operator ==(GCHandle left, GCHandle right) => left._handle == right._handle;

        public static bool operator !=(GCHandle left, GCHandle right) => left._handle != right._handle;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsPinned(IntPtr handle) =>
            (handle.ToInt32() & 3) == (int)GCHandleType.Pinned;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ThrowIfInvalid(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("The GC handle is not initialized.");
            }
        }
    }
}
