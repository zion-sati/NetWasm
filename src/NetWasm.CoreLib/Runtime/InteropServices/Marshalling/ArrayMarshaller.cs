// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices.Marshalling
{
    [CustomMarshaller(typeof(CustomMarshallerAttribute.GenericPlaceholder[]),
        MarshalMode.Default,
        typeof(ArrayMarshaller<,>))]
    [CustomMarshaller(typeof(CustomMarshallerAttribute.GenericPlaceholder[]),
        MarshalMode.ManagedToUnmanagedIn,
        typeof(ArrayMarshaller<,>.ManagedToUnmanagedIn))]
    [ContiguousCollectionMarshaller]
    public static unsafe class ArrayMarshaller<T, TUnmanagedElement>
        where TUnmanagedElement : unmanaged
    {
        public static TUnmanagedElement* AllocateContainerForUnmanagedElements(
            T[]? managed,
            out int numElements)
        {
            if (managed is null)
            {
                numElements = 0;
                return null;
            }

            numElements = managed.Length;
            var spaceToAllocate = Math.Max(checked(sizeof(TUnmanagedElement) * numElements), 1);
            return (TUnmanagedElement*)Marshal.AllocCoTaskMem(spaceToAllocate);
        }

        public static ReadOnlySpan<T> GetManagedValuesSource(T[]? managed) => managed;

        public static Span<TUnmanagedElement> GetUnmanagedValuesDestination(
            TUnmanagedElement* unmanaged,
            int numElements) =>
            unmanaged is null ? [] : new Span<TUnmanagedElement>(unmanaged, numElements);

        public static T[]? AllocateContainerForManagedElements(
            TUnmanagedElement* unmanaged,
            int numElements) =>
            unmanaged is null ? null : new T[numElements];

        public static Span<T> GetManagedValuesDestination(T[]? managed) => managed;

        public static ReadOnlySpan<TUnmanagedElement> GetUnmanagedValuesSource(
            TUnmanagedElement* unmanaged,
            int numElements) =>
            unmanaged is null ? [] : new ReadOnlySpan<TUnmanagedElement>(unmanaged, numElements);

        public static void Free(TUnmanagedElement* unmanaged) =>
            Marshal.FreeCoTaskMem((IntPtr)unmanaged);

        public ref struct ManagedToUnmanagedIn
        {
            private T[]? _managedArray;
            private TUnmanagedElement* _allocatedMemory;
            private Span<TUnmanagedElement> _span;

            public static int BufferSize => 0x200 / sizeof(TUnmanagedElement);

            public void FromManaged(T[]? array, Span<TUnmanagedElement> buffer)
            {
                _allocatedMemory = null;
                if (array is null)
                {
                    _managedArray = null;
                    _span = default;
                    return;
                }

                _managedArray = array;
                if (array.Length <= buffer.Length)
                {
                    _span = buffer[0..array.Length];
                }
                else
                {
                    var bufferSize = checked(array.Length * sizeof(TUnmanagedElement));
                    var spaceToAllocate = Math.Max(bufferSize, 1);
                    _allocatedMemory = (TUnmanagedElement*)NativeMemory.Alloc((nuint)spaceToAllocate);
                    _span = new Span<TUnmanagedElement>(_allocatedMemory, array.Length);
                }
            }

            public ReadOnlySpan<T> GetManagedValuesSource() => _managedArray;

            public Span<TUnmanagedElement> GetUnmanagedValuesDestination() => _span;

            public ref TUnmanagedElement GetPinnableReference() =>
                ref MemoryMarshal.GetReference(_span);

            public TUnmanagedElement* ToUnmanaged() =>
                (TUnmanagedElement*)Unsafe.AsPointer(ref GetPinnableReference());

            public void Free() => NativeMemory.Free(_allocatedMemory);

            public static ref T GetPinnableReference(T[]? array)
            {
                if (array is null)
                {
                    return ref Unsafe.NullRef<T>();
                }

                return ref MemoryMarshal.GetArrayDataReference(array);
            }
        }
    }
}
