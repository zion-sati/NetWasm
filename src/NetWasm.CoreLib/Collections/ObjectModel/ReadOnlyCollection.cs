// Portions derived from dotnet/runtime System.Private.CoreLib's
// ReadOnlyCollection<T> at commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements; the .NET
// Foundation licenses this file to you under the MIT license.

namespace System.Collections.ObjectModel
{
    public class ReadOnlyCollection<T> : Collections.Generic.ICollection<T>,
        Collections.Generic.IEnumerable<T>, Collections.Generic.IList<T>,
        Collections.Generic.IReadOnlyCollection<T>, Collections.Generic.IReadOnlyList<T>,
        Collections.ICollection, Collections.IEnumerable, Collections.IList
    {
        private readonly Collections.Generic.IList<T> _list;

        public ReadOnlyCollection(Collections.Generic.IList<T> list)
        {
            _list = list ?? throw new ArgumentNullException();
        }

        public static ReadOnlyCollection<T> Empty { get; } =
            new(new Collections.Generic.List<T>());

        protected Collections.Generic.IList<T> Items => _list;
        public int Count { get => _list.Count; }
        public T this[int index] { get => _list[index]; }

        T Collections.Generic.IList<T>.this[int index]
        {
            get => _list[index];
            set => throw new NotSupportedException();
        }

        bool Collections.Generic.ICollection<T>.IsReadOnly => true;
        public bool Contains(T value) => _list.Contains(value);
        public int IndexOf(T value) => _list.IndexOf(value);
        public void CopyTo(T[] array, int index) => _list.CopyTo(array, index);
        public Collections.Generic.IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
        Collections.IEnumerator Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        void Collections.Generic.ICollection<T>.Add(T value) => ThrowReadOnly();
        void Collections.Generic.ICollection<T>.Clear() => ThrowReadOnly();
        bool Collections.Generic.ICollection<T>.Remove(T value) => ThrowReadOnly<bool>();
        void Collections.Generic.IList<T>.Insert(int index, T value) => ThrowReadOnly();
        void Collections.Generic.IList<T>.RemoveAt(int index) => ThrowReadOnly();

        bool Collections.IList.IsReadOnly => true;
        bool Collections.IList.IsFixedSize => true;
        object? Collections.IList.this[int index]
        {
            get => _list[index];
            set => ThrowReadOnly();
        }

        int Collections.IList.Add(object? value) => ThrowReadOnly<int>();
        void Collections.IList.Clear() => ThrowReadOnly();
        bool Collections.IList.Contains(object? value) =>
            IsCompatibleObject(value) && _list.Contains(ConvertObject(value));
        int Collections.IList.IndexOf(object? value) =>
            IsCompatibleObject(value) ? _list.IndexOf(ConvertObject(value)) : -1;
        void Collections.IList.Insert(int index, object? value) => ThrowReadOnly();
        void Collections.IList.Remove(object? value) => ThrowReadOnly();
        void Collections.IList.RemoveAt(int index) => ThrowReadOnly();

        bool Collections.ICollection.IsSynchronized => false;
        object Collections.ICollection.SyncRoot => this;
        void Collections.ICollection.CopyTo(Array array, int index)
        {
            if (array is T[] typed)
            {
                CopyTo(typed, index);
                return;
            }
            throw new ArgumentException();
        }

        private static bool IsCompatibleObject(object? value) =>
            value is T || (value is null && default(T) is null);

        private static T ConvertObject(object? value)
        {
            if (value is T item)
            {
                return item;
            }
            if (value is null && default(T) is null)
            {
                return default!;
            }
            throw new ArgumentException();
        }

        private static void ThrowReadOnly() => throw new NotSupportedException();

        private static TResult ThrowReadOnly<TResult>() =>
            throw new NotSupportedException();
    }

    // Collection-builder helpers introduced by the same upstream source file.
    public static class ReadOnlyCollection
    {
        public static ReadOnlyCollection<T> CreateCollection<T>(params ReadOnlySpan<T> values) =>
            values.IsEmpty ? ReadOnlyCollection<T>.Empty :
            new ReadOnlyCollection<T>(values.ToArray());

        public static ReadOnlySet<T> CreateSet<T>(params ReadOnlySpan<T> values)
        {
            if (values.IsEmpty)
            {
                return ReadOnlySet<T>.Empty;
            }
            var set = ReadOnlySet<T>.CreateMutableSet();
            for (var index = 0; index < values.Length; index++)
            {
                set.Add(values[index]);
            }
            return new ReadOnlySet<T>(set);
        }
    }
}
