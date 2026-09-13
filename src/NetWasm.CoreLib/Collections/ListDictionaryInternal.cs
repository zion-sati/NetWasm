// Portions derived from dotnet/runtime System.Private.CoreLib ListDictionaryInternal
// at commit 811225a482702af7ecc35d817966bc70b88a3a23.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Collections
{
    public class ListDictionaryInternal : ICollection, IDictionary, IEnumerable
    {
        private Node? _head;
        private int _count;
        private int _version;

        public ListDictionaryInternal() { }

        public object? this[object key]
        {
            get { return Find(key)?.Value; }
            set
            {
                ValidateKey(key);
                var node = Find(key);
                _version++;
                if (node is not null) { node.Value = value; return; }
                AddNode(new Node(key, value));
            }
        }

        public int Count
        {
            get => _count;
        }

        public ICollection Keys
        {
            get => new NodeCollection(this, true);
        }

        public ICollection Values
        {
            get => new NodeCollection(this, false);
        }

        public bool IsReadOnly
        {
            get => false;
        }

        public bool IsFixedSize
        {
            get => false;
        }

        public bool IsSynchronized
        {
            get => false;
        }

        public object SyncRoot
        {
            get => this;
        }

        public void Add(object key, object? value)
        {
            ValidateKey(key);
            if (Find(key) is not null) throw new ArgumentException();
            _version++;
            AddNode(new Node(key, value));
        }

        public void Clear()
        {
            _head = null;
            _count = 0;
            _version++;
        }

        public bool Contains(object key) => Find(key) is not null;

        public void Remove(object key)
        {
            ValidateKey(key);
            Node? previous = null;
            for (var current = _head; current is not null; current = current.Next)
            {
                if (!object.Equals(current.Key, key)) { previous = current; continue; }
                if (previous is null) _head = current.Next;
                else previous.Next = current.Next;
                _count--;
                _version++;
                return;
            }
        }

        public void CopyTo(Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (array is object[] objects)
            {
                if (index > objects.Length - _count) throw new ArgumentException();
                var destination = index;
                for (var current = _head; current is not null; current = current.Next)
                    objects[destination++] = new DictionaryEntry(current.Key, current.Value);
                return;
            }
            if (array is DictionaryEntry[] entries)
            {
                if (index > entries.Length - _count) throw new ArgumentException();
                var destination = index;
                for (var current = _head; current is not null; current = current.Next)
                    entries[destination++] = new DictionaryEntry(current.Key, current.Value);
                return;
            }
            throw new ArgumentException();
        }

        public IDictionaryEnumerator GetEnumerator() => new Enumerator(this);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private Node? Find(object key)
        {
            ValidateKey(key);
            for (var current = _head; current is not null; current = current.Next)
                if (object.Equals(current.Key, key)) return current;
            return null;
        }

        private void AddNode(Node node)
        {
            if (_head is null) _head = node;
            else
            {
                var tail = _head;
                while (tail.Next is not null) tail = tail.Next;
                tail.Next = node;
            }
            _count++;
        }

        private static void ValidateKey(object key)
        { if (key is null) throw new ArgumentNullException(nameof(key)); }

        private sealed class Node
        {
            internal Node(object key, object? value) { Key = key; Value = value; }
            internal object Key;
            internal object? Value;
            internal Node? Next;
        }

        private sealed class Enumerator : IDictionaryEnumerator
        {
            private readonly ListDictionaryInternal _owner;
            private readonly int _version;
            private Node? _current;
            private bool _started;

            internal Enumerator(ListDictionaryInternal owner) { _owner = owner; _version = owner._version; }
            public object Current => Entry;
            public DictionaryEntry Entry
            {
                get
                {
                    EnsureCurrent();
                    return new DictionaryEntry(_current!.Key, _current.Value);
                }
            }
            public object Key { get { EnsureCurrent(); return _current!.Key; } }
            public object? Value { get { EnsureCurrent(); return _current!.Value; } }
            public bool MoveNext()
            {
                EnsureVersion();
                _current = !_started ? _owner._head : _current?.Next;
                _started = true;
                return _current is not null;
            }
            public void Reset() { EnsureVersion(); _current = null; _started = false; }
            private void EnsureVersion() { if (_version != _owner._version) throw new InvalidOperationException(); }
            private void EnsureCurrent() { EnsureVersion(); if (_current is null) throw new InvalidOperationException(); }
        }

        private sealed class NodeCollection : ICollection
        {
            private readonly ListDictionaryInternal _owner;
            private readonly bool _keys;
            internal NodeCollection(ListDictionaryInternal owner, bool keys) { _owner = owner; _keys = keys; }
            public int Count => _owner._count;
            public object SyncRoot => _owner.SyncRoot;
            public bool IsSynchronized => false;
            public void CopyTo(Array array, int index)
            {
                ArgumentNullException.ThrowIfNull(array);
                if (array is not object[] target || index < 0 || index > target.Length - Count) throw new ArgumentException();
                var destination = index;
                for (var current = _owner._head; current is not null; current = current.Next)
                    target[destination++] = _keys ? current.Key : current.Value!;
            }
            public IEnumerator GetEnumerator() => new NodeEnumerator(_owner, _keys);
        }

        private sealed class NodeEnumerator : IEnumerator
        {
            private readonly ListDictionaryInternal _owner;
            private readonly bool _keys;
            private readonly int _version;
            private Node? _current;
            private bool _started;
            internal NodeEnumerator(ListDictionaryInternal owner, bool keys) { _owner = owner; _keys = keys; _version = owner._version; }
            public object Current { get { if (_current is null) throw new InvalidOperationException(); return _keys ? _current.Key : _current.Value!; } }
            public bool MoveNext() { if (_version != _owner._version) throw new InvalidOperationException(); _current = !_started ? _owner._head : _current?.Next; _started = true; return _current is not null; }
            public void Reset() { if (_version != _owner._version) throw new InvalidOperationException(); _current = null; _started = false; }
        }
    }
}
