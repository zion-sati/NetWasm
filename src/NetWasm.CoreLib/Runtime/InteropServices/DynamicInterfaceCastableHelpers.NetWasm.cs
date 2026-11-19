// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices
{
    // What castclass, isinst, an array store and interface dispatch do with an object whose class implements
    // IDynamicInterfaceCastable, as CoreCLR's runtime does through its DynamicInterfaceCastableHelpers. NetWasm has no
    // runtime to consult the object, so once a program allocates such a class the compiler routes its interface casts
    // and array stores here, and each interface call asks Implementation for a receiver whose class does not implement
    // the interface, then calls the implementation interface's ([DynamicInterfaceCastableImplementation]) method. A
    // value type that implements the interface is ignored, as on .NET.
    internal static class DynamicInterfaceCastableHelpers
    {
        // castclass T, for an interface T.
        [StackTraceHidden]
        internal static T? CastClass<T>(object? value) where T : class
        {
            if (value is not T && AsCastable(value) is { } castable)
            {
                IsInterfaceImplemented(castable, typeof(T).TypeHandle, throwIfNotImplemented: true);
                return Unsafe.As<T>(value);
            }
            return (T?)value;
        }

        // isinst T, for an interface T.
        internal static T? IsInstance<T>(object? value) where T : class
        {
            if (value is not T && AsCastable(value) is { } castable)
            {
                return IsInterfaceImplemented(castable, typeof(T).TypeHandle, throwIfNotImplemented: false)
                    ? Unsafe.As<T>(value)
                    : null;
            }
            return value as T;
        }

        // stelem.ref: an object the array's element type does not admit is stored when it implements the element
        // interface dynamically, as CoreCLR's StelemRef asks it.
        [StackTraceHidden]
        internal static void StoreElement(object?[] array, int index, object? value)
        {
            if (value is not null && AsCastable(value) is { } castable &&
                array.GetType().GetElementType() is { IsInterface: true } elementType && !elementType.IsInstanceOfType(value))
            {
                if ((uint)index >= (uint)array.Length)
                {
                    ThrowHelper.ThrowIndexOutOfRangeException();
                }
                if (!IsInterfaceImplemented(castable, elementType.TypeHandle, throwIfNotImplemented: false))
                {
                    ThrowHelper.ThrowArrayTypeMismatchException();
                }
                Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), index) = value;
                return;
            }
            array[index] = value;
        }

        // The handle the receiver names as the implementation of TInterface, for an interface call on a receiver whose
        // class does not implement it. The call compares it with the implementation interfaces the program has, each a
        // valid answer by construction; a handle that matches none goes to InvalidImplementation. One that neither
        // implements the interface nor casts dynamically fails the cast the call implies.
        [StackTraceHidden]
        internal static RuntimeTypeHandle Implementation<TInterface>(object receiver) where TInterface : class
        {
            if (AsCastable(receiver) is { } castable)
            {
                RuntimeTypeHandle handle = castable.GetInterfaceImplementation(typeof(TInterface).TypeHandle);
                if (handle.Equals(default))
                    throw new InvalidCastException(System.SR.Format(System.SR.InvalidCast_FromTo, castable.GetType(), typeof(TInterface)));
                return handle;
            }
            _ = (TInterface)receiver;
            return default;
        }

        internal static bool Is(RuntimeTypeHandle implementation, RuntimeTypeHandle candidate) =>
            implementation.Equals(candidate);

        // A valid implementation interface that provides no implementation of the method the call names.
        [StackTraceHidden]
        internal static Exception MissingImplementation() => new EntryPointNotFoundException();

        // The exception for an implementation handle that matches no implementation interface providing the called
        // method: what .NET's runtime throws for an invalid one, or, for a valid one without the method,
        // EntryPointNotFoundException.
        [StackTraceHidden]
        internal static Exception InvalidImplementation<TInterface>(object receiver, RuntimeTypeHandle implementation) where TInterface : class
        {
            Type requested = typeof(TInterface);
            Type implType = Type.GetTypeFromHandle(implementation)!;
            if (!implType.IsInterface)
                return new InvalidOperationException(System.SR.Format(System.SR.IDynamicInterfaceCastable_NotInterface, implType.ToString()));

            if (!implType.IsDefined(typeof(DynamicInterfaceCastableImplementationAttribute), inherit: false))
                return new InvalidOperationException(System.SR.Format(System.SR.IDynamicInterfaceCastable_MissingImplementationAttribute, implType, nameof(DynamicInterfaceCastableImplementationAttribute)));

            if (!implType.IsAssignableTo(requested))
                return new InvalidOperationException(System.SR.Format(System.SR.IDynamicInterfaceCastable_DoesNotImplementRequested, implType, requested));

            return new EntryPointNotFoundException();
        }

        [StackTraceHidden]
        internal static bool IsInterfaceImplemented(IDynamicInterfaceCastable castable, RuntimeTypeHandle interfaceType,
            bool throwIfNotImplemented)
        {
            bool isImplemented = castable.IsInterfaceImplemented(interfaceType, throwIfNotImplemented);
            if (!isImplemented && throwIfNotImplemented)
                throw new InvalidCastException(System.SR.Format(System.SR.InvalidCast_FromTo, castable.GetType(),
                    Type.GetTypeFromHandle(interfaceType)));

            return isImplemented;
        }

        private static IDynamicInterfaceCastable? AsCastable(object? value) =>
            value is not ValueType ? value as IDynamicInterfaceCastable : null;
    }
}
