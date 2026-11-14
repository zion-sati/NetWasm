// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Preserve managed state, validation and disposal on the single-thread reactor.
// A blocking wait throws, as on .NET 11's single-threaded runtimes; the wait handle is a ManualResetEvent.
namespace System.Threading;

public class ManualResetEventSlim : IDisposable
{
    private bool _isSet;
    private bool _disposed;
    private ManualResetEvent? _event;

    public ManualResetEventSlim() : this(false) { }
    public ManualResetEventSlim(bool initialState) : this(initialState, 1) { }
    public ManualResetEventSlim(bool initialState, int spinCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spinCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(spinCount, (1 << 11) - 1);
        _isSet = initialState;
    }

    public bool IsSet => _isSet;
    // Upstream forces one spin on a single processor, even for an explicit count.
    public int SpinCount => 1;
    public WaitHandle WaitHandle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // As upstream: the event is created on first use, in the current state, and Set and Reset keep it there.
            return _event ??= new ManualResetEvent(_isSet);
        }
    }

    // Upstream Set intentionally remains usable after disposal; Reset does not.
    public void Set()
    {
        _isSet = true;
        _event?.Set();
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _isSet = false;
        _event?.Reset();
    }

    public void Wait() => Wait(Timeout.Infinite, CancellationToken.None);
    public void Wait(CancellationToken cancellationToken) => Wait(Timeout.Infinite, cancellationToken);
    public bool Wait(int millisecondsTimeout) => Wait(millisecondsTimeout, CancellationToken.None);
    public bool Wait(TimeSpan timeout) => Wait(timeout, CancellationToken.None);
    public bool Wait(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var totalMilliseconds = (long)timeout.TotalMilliseconds;
        ArgumentOutOfRangeException.ThrowIfLessThan(totalMilliseconds, -1, nameof(timeout));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(totalMilliseconds, int.MaxValue, nameof(timeout));
        return Wait((int)totalMilliseconds, cancellationToken);
    }

    public bool Wait(int millisecondsTimeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
        if (_isSet) return true;
        if (millisecondsTimeout == 0) return false;
        throw new PlatformNotSupportedException("ManualResetEventSlim blocking waits are not supported on this NetWasm platform.");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    protected virtual void Dispose(bool disposing)
    {
        _disposed = true;
        if (disposing)
        {
            _event?.Dispose();
            _event = null;
        }
    }
}
