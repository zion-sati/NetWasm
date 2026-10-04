// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace System.Threading;

// Adapted from System.Private.CoreLib's ThreadLocal<T>. A NetWasm reactor has
// one managed thread, so each instance needs one slot rather than a global TLS
// table. Shared-memory managed threads require per-thread storage before this
// implementation can be used there. Async continuations on this reactor share
// the slot, as ThreadLocal does on a single desktop thread.
public class ThreadLocal<T> : IDisposable
{
    private readonly Func<T>? _valueFactory;
    private readonly bool _trackAllValues;
    private bool _initialized;
    private bool _valueCreated;
    private T? _value;

    public ThreadLocal() : this(false) { }

    public ThreadLocal(bool trackAllValues)
    {
        _trackAllValues = trackAllValues;
        _initialized = true;
    }

    public ThreadLocal(Func<T> valueFactory) : this(valueFactory, false) { }

    public ThreadLocal(Func<T> valueFactory, bool trackAllValues)
    {
        // Keep failed construction uninitialized even if a derived finalizer
        // observes this object. Upstream validates before initializing its slot.
        ArgumentNullException.ThrowIfNull(valueFactory);
        _valueFactory = valueFactory;
        _trackAllValues = trackAllValues;
        _initialized = true;
    }

    ~ThreadLocal() => Dispose(false);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        _initialized = false;
        _valueCreated = false;
        _value = default;
    }

    [MaybeNull]
    public T Value
    {
        get
        {
            ObjectDisposedException.ThrowIf(!_initialized, this);
            if (_valueCreated)
                return _value;

            var value = default(T);
            if (_valueFactory is not null)
            {
                value = _valueFactory();
                // Match upstream's post-factory check, including assignment
                // or finite recursive initialization and disposal by the factory.
                if (IsValueCreated)
                    throw new InvalidOperationException("ValueFactory attempted to access the Value property of this instance.");
            }

            Value = value!;
            return value;
        }
        set
        {
            ObjectDisposedException.ThrowIf(!_initialized, this);
            _value = value;
            _valueCreated = true;
        }
    }

    public bool IsValueCreated
    {
        get
        {
            ObjectDisposedException.ThrowIf(!_initialized, this);
            return _valueCreated;
        }
    }

    public IList<T> Values
    {
        get
        {
            if (!_trackAllValues)
                throw new InvalidOperationException("The ThreadLocal object is not tracking values. Use a constructor that enables tracking.");
            ObjectDisposedException.ThrowIf(!_initialized, this);
            var values = new List<T>();
            if (_valueCreated)
                values.Add(_value!);
            return values;
        }
    }

    public override string ToString() => Value!.ToString()!;
}
