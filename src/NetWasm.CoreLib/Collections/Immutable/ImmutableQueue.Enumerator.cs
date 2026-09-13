// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;
using System.ComponentModel;

namespace System.Collections.Immutable;

public sealed partial class ImmutableQueue<T>
{
    /// <summary>Enumerates a queue with no memory allocations.</summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public struct Enumerator
    {
        private readonly ImmutableQueue<T> _originalQueue;
        private ImmutableStack<T>? _remainingForwardsStack;
        private ImmutableStack<T>? _remainingBackwardsStack;

        internal Enumerator(ImmutableQueue<T> queue)
        {
            _originalQueue = queue;
            _remainingForwardsStack = null;
            _remainingBackwardsStack = null;
        }

        public T Current
        {
            get
            {
                if (_remainingForwardsStack is null)
                {
                    throw new InvalidOperationException();
                }

                if (!_remainingForwardsStack.IsEmpty)
                {
                    return _remainingForwardsStack.Peek();
                }

                if (!_remainingBackwardsStack!.IsEmpty)
                {
                    return _remainingBackwardsStack.Peek();
                }

                throw new InvalidOperationException();
            }
        }

        public bool MoveNext()
        {
            if (_remainingForwardsStack is null)
            {
                _remainingForwardsStack = _originalQueue._forwards;
                _remainingBackwardsStack = _originalQueue.BackwardsReversed;
            }
            else if (!_remainingForwardsStack.IsEmpty)
            {
                _remainingForwardsStack = _remainingForwardsStack.Pop();
            }
            else if (!_remainingBackwardsStack!.IsEmpty)
            {
                _remainingBackwardsStack = _remainingBackwardsStack.Pop();
            }

            return !_remainingForwardsStack.IsEmpty || !_remainingBackwardsStack!.IsEmpty;
        }
    }

    private sealed class EnumeratorObject : IEnumerator<T>
    {
        private readonly ImmutableQueue<T> _originalQueue;
        private ImmutableStack<T>? _remainingForwardsStack;
        private ImmutableStack<T>? _remainingBackwardsStack;
        private bool _disposed;

        internal EnumeratorObject(ImmutableQueue<T> queue)
        {
            _originalQueue = queue;
        }

        public T Current
        {
            get
            {
                ThrowIfDisposed();
                if (_remainingForwardsStack is null)
                {
                    throw new InvalidOperationException();
                }

                if (!_remainingForwardsStack.IsEmpty)
                {
                    return _remainingForwardsStack.Peek();
                }

                if (!_remainingBackwardsStack!.IsEmpty)
                {
                    return _remainingBackwardsStack.Peek();
                }

                throw new InvalidOperationException();
            }
        }

        object? System.Collections.IEnumerator.Current => Current;

        public bool MoveNext()
        {
            ThrowIfDisposed();
            if (_remainingForwardsStack is null)
            {
                _remainingForwardsStack = _originalQueue._forwards;
                _remainingBackwardsStack = _originalQueue.BackwardsReversed;
            }
            else if (!_remainingForwardsStack.IsEmpty)
            {
                _remainingForwardsStack = _remainingForwardsStack.Pop();
            }
            else if (!_remainingBackwardsStack!.IsEmpty)
            {
                _remainingBackwardsStack = _remainingBackwardsStack.Pop();
            }

            return !_remainingForwardsStack.IsEmpty || !_remainingBackwardsStack!.IsEmpty;
        }

        public void Reset()
        {
            ThrowIfDisposed();
            _remainingBackwardsStack = null;
            _remainingForwardsStack = null;
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
