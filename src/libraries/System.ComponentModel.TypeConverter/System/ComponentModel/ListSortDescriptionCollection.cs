// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace System.ComponentModel
{
    public class ListSortDescriptionCollection : IList
    {
        private readonly ArrayList _sorts = new ArrayList();

        public ListSortDescriptionCollection()
        {
        }

        public ListSortDescriptionCollection(ListSortDescription?[]? sorts)
        {
            if (sorts != null)
            {
                for (int i = 0; i < sorts.Length; i++)
                {
                    _sorts.Add(sorts[i]);
                }
            }
        }

        public ListSortDescription? this[int index]
        {
            get => (ListSortDescription?)_sorts[index];
            set => throw new InvalidOperationException("Once a ListSortDescriptionCollection has been created it can't be modified.");
        }

        // IList implementation

        bool IList.IsFixedSize => true;

        bool IList.IsReadOnly => true;

        object? IList.this[int index]
        {
            get => this[index];
            set => throw new InvalidOperationException("Once a ListSortDescriptionCollection has been created it can't be modified.");
        }

        int IList.Add(object? value) => throw new InvalidOperationException("Once a ListSortDescriptionCollection has been created it can't be modified.");

        void IList.Clear() => throw new InvalidOperationException("Once a ListSortDescriptionCollection has been created it can't be modified.");

        public bool Contains(object? value) => ((IList)_sorts).Contains(value);

        public int IndexOf(object? value) => ((IList)_sorts).IndexOf(value);

        void IList.Insert(int index, object? value) => throw new InvalidOperationException("Once a ListSortDescriptionCollection has been created it can't be modified.");

        void IList.Remove(object? value) => throw new InvalidOperationException("Once a ListSortDescriptionCollection has been created it can't be modified.");

        void IList.RemoveAt(int index) => throw new InvalidOperationException("Once a ListSortDescriptionCollection has been created it can't be modified.");

        // ICollection

        public int Count => _sorts.Count;

        bool ICollection.IsSynchronized => true;

        object ICollection.SyncRoot => this;

        public void CopyTo(Array array, int index) => _sorts.CopyTo(array, index);

        // IEnumerable
        IEnumerator IEnumerable.GetEnumerator() => _sorts.GetEnumerator();
    }
}
