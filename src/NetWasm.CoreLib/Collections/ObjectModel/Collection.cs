// Adapted from dotnet/runtime System.Private.CoreLib at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Collections.ObjectModel
{
    public class Collection<T> : Collections.Generic.ICollection<T>,
        Collections.Generic.IEnumerable<T>, Collections.Generic.IList<T>,
        Collections.Generic.IReadOnlyCollection<T>, Collections.Generic.IReadOnlyList<T>,
        Collections.ICollection, Collections.IEnumerable, Collections.IList
    {
        private readonly Collections.Generic.IList<T> _items;

        public Collection()
        {
            _items = new Collections.Generic.List<T>();
        }

        public Collection(Collections.Generic.IList<T> list)
        {
            _items = list ?? throw new ArgumentNullException();
        }

        public int Count { get => _items.Count; }
        protected Collections.Generic.IList<T> Items => _items;
        bool Collections.Generic.ICollection<T>.IsReadOnly => _items.IsReadOnly;

        public T this[int index]
        {
            get => _items[index];
            set => SetItem(index, value);
        }

        public void Add(T item) => InsertItem(Count, item);
        public void Clear() => ClearItems();
        public bool Contains(T item) => _items.Contains(item);
        public void CopyTo(T[] array, int index) => _items.CopyTo(array, index);
        public int IndexOf(T item) => _items.IndexOf(item);
        public void Insert(int index, T item) => InsertItem(index, item);

        public bool Remove(T item)
        {
            var index = _items.IndexOf(item);
            if (index < 0)
            {
                return false;
            }
            RemoveItem(index);
            return true;
        }

        public void RemoveAt(int index) => RemoveItem(index);

        protected virtual void ClearItems() => _items.Clear();
        protected virtual void InsertItem(int index, T item) => _items.Insert(index, item);
        protected virtual void RemoveItem(int index) => _items.RemoveAt(index);
        protected virtual void SetItem(int index, T item) => _items[index] = item;

        public Collections.Generic.IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        Collections.IEnumerator Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        bool Collections.IList.IsReadOnly => _items.IsReadOnly;
        bool Collections.IList.IsFixedSize => _items.IsReadOnly;
        object? Collections.IList.this[int index]
        {
            get => this[index];
            set => this[index] = ConvertObject(value);
        }

        int Collections.IList.Add(object? value)
        {
            Add(ConvertObject(value));
            return Count - 1;
        }

        bool Collections.IList.Contains(object? value) =>
            IsCompatibleObject(value) && Contains(ConvertObject(value));

        int Collections.IList.IndexOf(object? value) =>
            IsCompatibleObject(value) ? IndexOf(ConvertObject(value)) : -1;

        void Collections.IList.Insert(int index, object? value) =>
            Insert(index, ConvertObject(value));

        void Collections.IList.Remove(object? value)
        {
            if (IsCompatibleObject(value))
            {
                Remove(ConvertObject(value));
            }
        }

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
    }
}
