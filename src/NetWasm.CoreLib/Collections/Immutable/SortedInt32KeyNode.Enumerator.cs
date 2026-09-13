// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
//
// The upstream pooled enumerator is intentionally reduced to a private stack
// because the NetWasm profile excludes the threading/pooling infrastructure.

using System.Collections.Generic;

namespace System.Collections.Immutable
{
    internal sealed partial class SortedInt32KeyNode<TValue>
    {
        /// <summary>
        /// Enumerates the contents of a binary tree.
        /// </summary>
        public struct Enumerator : IEnumerator<KeyValuePair<int, TValue>>, IDisposable
        {
            private SortedInt32KeyNode<TValue>? _root;
            private Stack<SortedInt32KeyNode<TValue>>? _stack;
            private SortedInt32KeyNode<TValue>? _current;

            internal Enumerator(SortedInt32KeyNode<TValue> root)
            {
                Requires.NotNull(root, nameof(root));

                _root = root;
                _current = null;
                _stack = root.IsEmpty ? null : new Stack<SortedInt32KeyNode<TValue>>(root.Height);
                if (_stack != null)
                {
                    this.PushLeft(root);
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

                    return _current!.Value;
                }
            }

            object IEnumerator.Current => this.Current;

            public void Dispose()
            {
                _root = null;
                _current = null;
                _stack = null;
            }

            public bool MoveNext()
            {
                this.ThrowIfDisposed();

                if (_stack != null && _stack.Count > 0)
                {
                    SortedInt32KeyNode<TValue> node = _stack.Pop();
                    _current = node;
                    this.PushLeft(node.Right!);
                    return true;
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
                    _stack.Clear();
                    this.PushLeft(_root!);
                }
            }

            internal void ThrowIfDisposed()
            {
                if (_root == null)
                {
                    Requires.FailObjectDisposed(this);
                }
            }

            private void PushLeft(SortedInt32KeyNode<TValue> node)
            {
                Requires.NotNull(node, nameof(node));
                Stack<SortedInt32KeyNode<TValue>> stack = _stack!;
                while (!node.IsEmpty)
                {
                    stack.Push(node);
                    node = node.Left!;
                }
            }
        }
    }
}
