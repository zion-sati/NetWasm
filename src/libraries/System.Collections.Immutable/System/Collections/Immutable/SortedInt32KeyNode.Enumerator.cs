// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.ComponentModel;

namespace System.Collections.Immutable
{
    internal sealed partial class SortedInt32KeyNode<TValue>
    {
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public struct Enumerator : IEnumerator<KeyValuePair<int, TValue>>, ISecurePooledObjectUser
        {
            private readonly int _poolUserId;
            private SortedInt32KeyNode<TValue> _root;
            private SecurePooledObject<Stack<RefAsValueType<SortedInt32KeyNode<TValue>>>>? _stack;
            private SortedInt32KeyNode<TValue>? _current;

            internal Enumerator(SortedInt32KeyNode<TValue> root)
            {
                Requires.NotNull(root, nameof(root));

                _root = root;
                _current = null;
                _poolUserId = SecureObjectPool.NewId();
                _stack = null;
                if (!_root.IsEmpty)
                {
                    if (!SecureObjectPool<Stack<RefAsValueType<SortedInt32KeyNode<TValue>>>, Enumerator>.TryTake(this, out _stack))
                    {
                        _stack = SecureObjectPool<Stack<RefAsValueType<SortedInt32KeyNode<TValue>>>, Enumerator>.PrepNew(this, new Stack<RefAsValueType<SortedInt32KeyNode<TValue>>>(root.Height));
                    }

                    this.PushLeft(_root);
                }
            }

            public KeyValuePair<int, TValue> Current
            {
                get
                {
                    this.ThrowIfDisposed();
                    if (_current == null)
                    {
                        ThrowHelper.ThrowInvalidOperationException();
                    }

                    return _current.Value;
                }
            }

            int ISecurePooledObjectUser.PoolUserId
            {
                get { return _poolUserId; }
            }

            object IEnumerator.Current
            {
                get { return this.Current; }
            }

            public void Dispose()
            {
                _root = null!;
                _current = null;
                if (_stack != null && _stack.TryUse(ref this, out Stack<RefAsValueType<SortedInt32KeyNode<TValue>>>? stack))
                {
                    stack.ClearFastWhenEmpty();
                    SecureObjectPool<Stack<RefAsValueType<SortedInt32KeyNode<TValue>>>, Enumerator>.TryAdd(this, _stack!);
                }

                _stack = null;
            }

            public bool MoveNext()
            {
                this.ThrowIfDisposed();

                if (_stack != null)
                {
                    Stack<RefAsValueType<SortedInt32KeyNode<TValue>>> stack = _stack.Use(ref this);
                    if (stack.Count > 0)
                    {
                        SortedInt32KeyNode<TValue> n = stack.Pop().Value;
                        _current = n;
                        this.PushLeft(n.Right!);
                        return true;
                    }
                }

                _current = null;
                return false;
            }

            public void Reset()
            {
                this.ThrowIfDisposed();

                _current = null;
                if (_stack != null)
                {
                    Stack<RefAsValueType<SortedInt32KeyNode<TValue>>> stack = _stack.Use(ref this);
                    stack.ClearFastWhenEmpty();
                    this.PushLeft(_root);
                }
            }

            internal void ThrowIfDisposed()
            {
                if (_root == null || (_stack != null && !_stack.IsOwned(ref this)))
                {
                    Requires.FailObjectDisposed(this);
                }
            }

            private void PushLeft(SortedInt32KeyNode<TValue> node)
            {
                Requires.NotNull(node, nameof(node));
                Stack<RefAsValueType<SortedInt32KeyNode<TValue>>> stack = _stack!.Use(ref this);
                while (!node.IsEmpty)
                {
                    stack.Push(new RefAsValueType<SortedInt32KeyNode<TValue>>(node));
                    node = node.Left!;
                }
            }
        }
    }
}
