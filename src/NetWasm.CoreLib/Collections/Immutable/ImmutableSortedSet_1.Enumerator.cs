// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.
//
// The upstream implementation uses the runtime's secure object pool. NetWasm
// keeps the same traversal semantics with a local single-reactor enumerator state.

using System.Collections.Generic;
using System.ComponentModel;

namespace System.Collections.Immutable
{
    public sealed partial class ImmutableSortedSet<T>
    {
        /// <summary>
        /// Enumerates the contents of a binary tree.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public struct Enumerator : IEnumerator<T>, IStrongEnumerator<T>
        {
            private readonly Builder? _builder;
            private readonly bool _reverse;
            private Node? _root;
            private EnumeratorState? _state;
            private Node? _current;
            private int _enumeratingBuilderVersion;

            private sealed class EnumeratorState
            {
                internal EnumeratorState(int capacity) => Stack = new Stack<Node>(capacity);

                internal Stack<Node> Stack { get; }

                internal bool Disposed { get; set; }
            }

            internal Enumerator(Node root, Builder? builder = null, bool reverse = false)
            {
                Requires.NotNull(root, nameof(root));

                _root = root;
                _builder = builder;
                _current = null;
                _reverse = reverse;
                _enumeratingBuilderVersion = builder != null ? builder.Version : -1;
                _state = root.IsEmpty ? null : new EnumeratorState(root.Height);
                if (_state != null)
                {
                    PushNext(root);
                }
            }

            /// <summary>
            /// Gets the current element.
            /// </summary>
            public T Current
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

            object IEnumerator.Current => Current!;

            /// <summary>
            /// Advances the enumerator to the next element.
            /// </summary>
            public bool MoveNext()
            {
                ThrowIfDisposed();
                ThrowIfChanged();

                if (_state != null && _state.Stack.Count > 0)
                {
                    Node node = _state.Stack.Pop();
                    _current = node;
                    PushNext(_reverse ? node.Left! : node.Right!);
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
                if (_state != null)
                {
                    _state.Stack.Clear();
                    PushNext(_root!);
                }
            }

            /// <summary>
            /// Releases the traversal state.
            /// </summary>
            public void Dispose()
            {
                _root = null;
                _current = null;
                if (_state != null)
                {
                    _state.Disposed = true;
                    _state.Stack.Clear();
                }

                _state = null;
            }

            private void ThrowIfDisposed()
            {
                if (_root == null || (_state != null && _state.Disposed))
                {
                    Requires.FailObjectDisposed(this);
                }
            }

            private void ThrowIfChanged()
            {
                if (_builder != null && _builder.Version != _enumeratingBuilderVersion)
                {
                    throw new InvalidOperationException(SR.CollectionModifiedDuringEnumeration);
                }
            }

            private void PushNext(Node node)
            {
                Requires.NotNull(node, nameof(node));
                Stack<Node> stack = _state!.Stack;
                while (!node.IsEmpty)
                {
                    stack.Push(node);
                    node = _reverse ? node.Right! : node.Left!;
                }
            }
        }
    }
}
