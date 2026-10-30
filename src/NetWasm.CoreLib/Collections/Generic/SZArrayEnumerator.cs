// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported in part from dotnet/runtime Array.Enumerators.cs at commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System
{
    internal abstract class SZGenericArrayEnumeratorBase : IDisposable
    {
        protected int _index;
        protected readonly int _endIndex;

        protected SZGenericArrayEnumeratorBase(int endIndex)
        {
            _index = -1;
            _endIndex = endIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            int index = _index + 1;
            if ((uint)index < (uint)_endIndex)
            {
                _index = index;
                return true;
            }
            _index = _endIndex;
            return false;
        }

        public void Reset() => _index = -1;

        public void Dispose()
        {
        }
    }

    internal sealed class SZGenericArrayEnumerator<T> : SZGenericArrayEnumeratorBase, IEnumerator<T>
    {
        private readonly T[]? _array;

        internal static readonly SZGenericArrayEnumerator<T> Empty = new SZGenericArrayEnumerator<T>(null, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal SZGenericArrayEnumerator(T[]? array, int endIndex)
            : base(endIndex)
        {
            Debug.Assert(array == null || endIndex == array.Length);
            _array = array;
        }

        public T Current
        {
            get
            {
                if ((uint)_index >= (uint)_endIndex)
                    ThrowHelper.ThrowInvalidOperationException_EnumCurrent(_index);
                return _array![_index];
            }
        }

        object? IEnumerator.Current => Current;
    }
}

namespace System.Collections.Generic
{
    internal static class SZArrayHelper
    {
        // A foreach over an array seen as IEnumerable<T> reaches this directly
        // once the receiver is known to be an array, and allocates its
        // enumerator either way, so the allocation is not a cold path to keep
        // out of callers; .NET's JIT treats this method as an intrinsic.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static IEnumerator<T> GetEnumerator<T>(T[] array) =>
            new SZGenericArrayEnumerator<T>(array, array.Length);

        internal static int GetCount<T>(T[] array) => array.Length;

        internal static bool GetIsReadOnly<T>(T[] array) => true;

        internal static T GetItem<T>(T[] array, int index)
        {
            if ((uint)index >= (uint)array.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRange_IndexMustBeLessException();
            }

            return array[index];
        }

        internal static void SetItem<T>(T[] array, int index, T value)
        {
            if ((uint)index >= (uint)array.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRange_IndexMustBeLessException();
            }

            array[index] = value;
        }

        internal static bool Contains<T>(T[] array, T value) =>
            System.Array.IndexOf(array, value) >= 0;

        internal static int IndexOf<T>(T[] array, T value) =>
            System.Array.IndexOf(array, value);

        internal static void CopyTo<T>(T[] array, T[] destination, int index) =>
            System.Array.Copy(
                (System.Array)array,
                0,
                (System.Array)destination,
                index,
                array.Length);

        internal static void Add<T>(T[] array, T value) => ThrowFixedSize();

        internal static void Clear<T>(T[] array) => ThrowFixedSize();

        internal static bool Remove<T>(T[] array, T value) =>
            ThrowFixedSize<bool>();

        internal static void Insert<T>(T[] array, int index, T value) =>
            ThrowFixedSize();

        internal static void RemoveAt<T>(T[] array, int index) => ThrowFixedSize();

        private static void ThrowFixedSize() =>
            throw new System.NotSupportedException();

        private static TResult ThrowFixedSize<TResult>() =>
            throw new System.NotSupportedException();
    }

    internal sealed class SZArrayEnumerator<T> : IEnumerator<T>
    {
        internal static readonly SZArrayEnumerator<T> Empty = new(System.Array.Empty<T>());

        private readonly T[] _array;
        private int _index;

        internal SZArrayEnumerator(T[] array)
        {
            _array = array;
            _index = -1;
        }

        public T Current
        {
            get
            {
                if (_index < 0 || _index >= _array.Length)
                {
                    throw new System.InvalidOperationException();
                }
                return _array[_index];
            }
        }

        object System.Collections.IEnumerator.Current => Current!;

        public bool MoveNext()
        {
            var next = _index + 1;
            if (next < _array.Length)
            {
                _index = next;
                return true;
            }
            _index = _array.Length;
            return false;
        }

        public void Dispose()
        {
        }

        void System.Collections.IEnumerator.Reset() =>
            throw new System.NotSupportedException();
    }
}
