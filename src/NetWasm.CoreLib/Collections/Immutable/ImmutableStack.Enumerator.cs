// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;
using System.ComponentModel;

namespace System.Collections.Immutable;

public sealed partial class ImmutableStack<T>
{
    /// <summary>Enumerates a stack with no memory allocations.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public struct Enumerator
    {
        private readonly ImmutableStack<T> _originalStack;
        private ImmutableStack<T>? _remainingStack;

        internal Enumerator(ImmutableStack<T> stack)
        {
            _originalStack = stack;
            _remainingStack = null;
        }

        public T Current
        {
            get
            {
                if (_remainingStack is null || _remainingStack.IsEmpty)
                {
                    throw new InvalidOperationException();
                }

                return _remainingStack.Peek();
            }
        }

        public bool MoveNext()
        {
            if (_remainingStack is null)
            {
                _remainingStack = _originalStack;
            }
            else if (!_remainingStack.IsEmpty)
            {
                _remainingStack = _remainingStack.Pop();
            }

            return !_remainingStack.IsEmpty;
        }
    }

    private sealed class EnumeratorObject : IEnumerator<T>
    {
        private readonly ImmutableStack<T> _originalStack;
        private ImmutableStack<T>? _remainingStack;
        private bool _disposed;

        internal EnumeratorObject(ImmutableStack<T> stack)
        {
            _originalStack = stack;
        }

        public T Current
        {
            get
            {
                ThrowIfDisposed();
                if (_remainingStack is null || _remainingStack.IsEmpty)
                {
                    throw new InvalidOperationException();
                }

                return _remainingStack.Peek();
            }
        }

        object? System.Collections.IEnumerator.Current => Current;

        public bool MoveNext()
        {
            ThrowIfDisposed();
            if (_remainingStack is null)
            {
                _remainingStack = _originalStack;
            }
            else if (!_remainingStack.IsEmpty)
            {
                _remainingStack = _remainingStack.Pop();
            }

            return !_remainingStack.IsEmpty;
        }

        public void Reset()
        {
            ThrowIfDisposed();
            _remainingStack = null;
        }

        public void Dispose() => _disposed = true;

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                Requires.FailObjectDisposed(this);
            }
        }
    }
}
