// Adapted from dotnet/runtime System.Collections LinkedList<T>.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Collections.Generic
{
    public class LinkedList<T> : ICollection<T>, IReadOnlyCollection<T>
    {
        internal LinkedListNode<T>? Head;
        internal int Version;
        private int _count;

        public LinkedList()
        {
        }

        public LinkedList(IEnumerable<T> collection)
        {
            if (collection == null)
            {
                throw new System.ArgumentNullException();
            }
            foreach (var item in collection)
            {
                AddLast(item);
            }
        }

        public int Count => _count;
        public LinkedListNode<T>? First => Head;
        public LinkedListNode<T>? Last => Head?.PreviousInternal;
        bool ICollection<T>.IsReadOnly => false;
        void ICollection<T>.Add(T value) => AddLast(value);

        public LinkedListNode<T> AddFirst(T value)
        {
            var node = new LinkedListNode<T>(this, value);
            if (Head == null)
            {
                InsertIntoEmptyList(node);
            }
            else
            {
                InsertBefore(Head, node);
                Head = node;
            }
            return node;
        }

        public void AddFirst(LinkedListNode<T> node)
        {
            ValidateNewNode(node);
            if (Head == null)
            {
                InsertIntoEmptyList(node);
            }
            else
            {
                InsertBefore(Head, node);
                Head = node;
            }
            node.ListInternal = this;
        }

        public LinkedListNode<T> AddLast(T value)
        {
            var node = new LinkedListNode<T>(this, value);
            if (Head == null)
            {
                InsertIntoEmptyList(node);
            }
            else
            {
                InsertBefore(Head, node);
            }
            return node;
        }

        public void AddLast(LinkedListNode<T> node)
        {
            ValidateNewNode(node);
            if (Head == null)
            {
                InsertIntoEmptyList(node);
            }
            else
            {
                InsertBefore(Head, node);
            }
            node.ListInternal = this;
        }

        public LinkedListNode<T> AddBefore(LinkedListNode<T> node, T value)
        {
            ValidateNode(node);
            var result = new LinkedListNode<T>(this, value);
            InsertBefore(node, result);
            if (node == Head)
            {
                Head = result;
            }
            return result;
        }

        public LinkedListNode<T> AddAfter(LinkedListNode<T> node, T value)
        {
            ValidateNode(node);
            var result = new LinkedListNode<T>(this, value);
            InsertBefore(node.NextInternal!, result);
            return result;
        }

        public bool Contains(T value) => Find(value) != null;

        public LinkedListNode<T>? Find(T value)
        {
            var node = Head;
            if (node == null)
            {
                return null;
            }
            do
            {
                if (EqualityComparer<T>.Default.Equals(node!.Value, value))
                {
                    return node;
                }
                node = node.NextInternal;
            }
            while (node != Head);
            return null;
        }

        public LinkedListNode<T>? FindLast(T value)
        {
            var last = Last;
            var node = last;
            if (node == null)
            {
                return null;
            }
            do
            {
                if (EqualityComparer<T>.Default.Equals(node!.Value, value))
                {
                    return node;
                }
                node = node.PreviousInternal;
            }
            while (node != last);
            return null;
        }

        public bool Remove(T value)
        {
            var node = Find(value);
            if (node == null)
            {
                return false;
            }
            Remove(node);
            return true;
        }

        public void Remove(LinkedListNode<T> node)
        {
            ValidateNode(node);
            if (node.NextInternal == node)
            {
                Head = null;
            }
            else
            {
                node.NextInternal!.PreviousInternal = node.PreviousInternal;
                node.PreviousInternal!.NextInternal = node.NextInternal;
                if (Head == node)
                {
                    Head = node.NextInternal;
                }
            }
            node.Invalidate();
            _count--;
            Version++;
        }

        public void RemoveFirst()
        {
            if (Head == null)
            {
                throw new System.InvalidOperationException();
            }
            Remove(Head);
        }

        public void RemoveLast()
        {
            if (Last == null)
            {
                throw new System.InvalidOperationException();
            }
            Remove(Last);
        }

        public void Clear()
        {
            var node = Head;
            while (node != null)
            {
                var next = node.Next;
                node.Invalidate();
                node = next;
            }
            Head = null;
            _count = 0;
            Version++;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new System.ArgumentNullException();
            }
            if (arrayIndex < 0 || arrayIndex > array.Length - _count)
            {
                throw new System.ArgumentOutOfRangeException();
            }
            var node = Head;
            if (node == null)
            {
                return;
            }
            do
            {
                array[arrayIndex++] = node!.Value;
                node = node.NextInternal;
            }
            while (node != Head);
        }

        public Enumerator GetEnumerator() => new(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        private void InsertBefore(LinkedListNode<T> node, LinkedListNode<T> newNode)
        {
            newNode.NextInternal = node;
            newNode.PreviousInternal = node.PreviousInternal;
            node.PreviousInternal!.NextInternal = newNode;
            node.PreviousInternal = newNode;
            Version++;
            _count++;
        }

        private void InsertIntoEmptyList(LinkedListNode<T> node)
        {
            node.NextInternal = node;
            node.PreviousInternal = node;
            Head = node;
            Version++;
            _count++;
        }

        private void ValidateNode(LinkedListNode<T> node)
        {
            if (node == null)
            {
                throw new System.ArgumentNullException();
            }
            if (node.ListInternal != this)
            {
                throw new System.InvalidOperationException();
            }
        }

        private static void ValidateNewNode(LinkedListNode<T> node)
        {
            if (node == null)
            {
                throw new System.ArgumentNullException();
            }
            if (node.ListInternal != null)
            {
                throw new System.InvalidOperationException();
            }
        }

        public struct Enumerator : IEnumerator<T>
        {
            private readonly LinkedList<T> _list;
            private readonly int _version;
            private LinkedListNode<T>? _node;
            private T _current;

            internal Enumerator(LinkedList<T> list)
            {
                _list = list;
                _version = list.Version;
                _node = list.Head;
                _current = default!;
            }

            public T Current => _current;
            object System.Collections.IEnumerator.Current => _current!;

            public bool MoveNext()
            {
                if (_version != _list.Version)
                {
                    throw new System.InvalidOperationException();
                }
                if (_node == null)
                {
                    _current = default!;
                    return false;
                }
                _current = _node.Value;
                _node = _node.NextInternal;
                if (_node == _list.Head)
                {
                    _node = null;
                }
                return true;
            }

            public void Dispose()
            {
            }

            void System.Collections.IEnumerator.Reset() =>
                throw new System.NotSupportedException();
        }
    }

    public sealed class LinkedListNode<T>
    {
        internal LinkedList<T>? ListInternal;
        internal LinkedListNode<T>? NextInternal;
        internal LinkedListNode<T>? PreviousInternal;

        public LinkedListNode(T value)
        {
            Value = value;
        }

        internal LinkedListNode(LinkedList<T> list, T value)
        {
            ListInternal = list;
            Value = value;
        }

        public LinkedList<T>? List => ListInternal;
        public LinkedListNode<T>? Next =>
            NextInternal == null || NextInternal == ListInternal!.Head ? null : NextInternal;
        public LinkedListNode<T>? Previous =>
            PreviousInternal == null || this == ListInternal!.Head ? null : PreviousInternal;
        public T Value { get; set; }

        internal void Invalidate()
        {
            ListInternal = null;
            NextInternal = null;
            PreviousInternal = null;
        }
    }
}
