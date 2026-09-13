// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
//
// The upstream implementation uses the runtime's secure object pool. NetWasm
// keeps the same traversal semantics with a CoreLib Stack.

using System.Collections.Generic;
using System.ComponentModel;

namespace System.Collections.Immutable
{
    public sealed partial class ImmutableSortedDictionary<TKey, TValue>
    {
        /// <summary>
        /// Enumerates the contents of a binary tree.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly Builder? _builder;
            private Node? _root;
            private Stack<Node>? _stack;
            private Node? _current;
            private int _enumeratingBuilderVersion;

            internal Enumerator(Node root, Builder? builder = null)
            {
                Requires.NotNull(root, nameof(root));

                _root = root;
                _builder = builder;
                _current = null;
                _enumeratingBuilderVersion = builder != null ? builder.Version : -1;
                _stack = root.IsEmpty ? null : new Stack<Node>(root.Height);
                if (_stack != null)
                {
                    PushLeft(root);
                }
            }

            /// <summary>
            /// Gets the current element.
            /// </summary>
            public KeyValuePair<TKey, TValue> Current
            {
                get
                {
                    ThrowIfDisposed();
                    if (_current == null)
                    {
                        ThrowHelper.ThrowInvalidOperationException();
                    }

                    return _current!.Value;
                }
            }

            object IEnumerator.Current => Current;

            /// <summary>
            /// Advances the enumerator to the next element.
            /// </summary>
            public bool MoveNext()
            {
                ThrowIfDisposed();
                ThrowIfChanged();

                if (_stack != null && _stack.TryPop(out Node? node))
                {
                    _current = node;
                    PushLeft(node.Right!);
                    return true;
                }

                _current = null;
                return false;
            }

            /// <summary>
            /// Restarts enumeration.
            /// </summary>
            public void Reset()
            {
                ThrowIfDisposed();

                _enumeratingBuilderVersion = _builder != null ? _builder.Version : -1;
                _current = null;
                if (_stack != null)
                {
                    _stack.Clear();
                    PushLeft(_root!);
                }
            }

            /// <summary>
            /// Releases the traversal state.
            /// </summary>
            public void Dispose()
            {
                _root = null;
                _current = null;
                _stack = null;
            }

            internal void ThrowIfDisposed()
            {
                if (_root == null)
                {
                    Requires.FailObjectDisposed(this);
                }
            }

            private void ThrowIfChanged()
            {
                if (_builder != null && _builder.Version != _enumeratingBuilderVersion)
                {
                    throw new InvalidOperationException(ImmutableDictionaryThrowHelper.CollectionModifiedDuringEnumeration);
                }
            }

            private void PushLeft(Node node)
            {
                Requires.NotNull(node, nameof(node));
                Stack<Node> stack = _stack!;
                while (!node.IsEmpty)
                {
                    stack.Push(node);
                    node = node.Left!;
                }
            }
        }
    }
}
