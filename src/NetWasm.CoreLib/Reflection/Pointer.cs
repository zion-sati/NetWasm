// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: NetWasm has no RuntimeType, so the box keeps the Type it was given. Every
// NetWasm Type is a runtime type, so only the pointer-type check remains.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace System.Reflection
{
    [CLSCompliant(false)]
    public sealed unsafe class Pointer : ISerializable
    {
        // CoreCLR: Do not add or remove fields without updating the ReflectionPointer class in runtimehandles.h
        private readonly void* _ptr;
        private readonly Type _ptrType;

        private Pointer(void* ptr, Type ptrType)
        {
            _ptr = ptr;
            _ptrType = ptrType;
        }

        public static object Box(void* ptr, Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            if (!type.IsPointer)
                throw new ArgumentException(SR.Arg_MustBePointer, nameof(ptr));
            return new Pointer(ptr, type);
        }

        /// <safety>Returns the pointer value previously stored in the boxed Pointer instance; it reads only a managed field and performs no dereference of the pointer.</safety>
        public static void* Unbox(object ptr)
        {
            if (ptr is Pointer p)
                return p._ptr;

            throw new ArgumentException(SR.Arg_MustBePointer, nameof(ptr));
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is Pointer pointer)
            {
                return _ptr == pointer._ptr;
            }

            return false;
        }

        public override int GetHashCode() => ((nuint)_ptr).GetHashCode();

        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            throw new PlatformNotSupportedException();
        }

        internal Type GetPointerType() => _ptrType;
        internal IntPtr GetPointerValue() => (IntPtr)_ptr;
    }
}
