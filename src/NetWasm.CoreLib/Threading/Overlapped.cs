// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Preserve managed descriptor state. Native allocation/packing is unavailable.
namespace System.Threading;

public unsafe class Overlapped
{
    private IAsyncResult? _asyncResult;
    private IntPtr _eventHandle;
    private int _offsetLow;
    private int _offsetHigh;
    public Overlapped() { }
    public Overlapped(int offsetLo, int offsetHi, IntPtr hEvent, IAsyncResult? ar)
    {
        _offsetLow = offsetLo; _offsetHigh = offsetHi; _eventHandle = hEvent; _asyncResult = ar;
    }
    [Obsolete("This constructor is not 64-bit compatible and has been deprecated. Use the constructor that accepts an IntPtr for the event handle instead.")]
    public Overlapped(int offsetLo, int offsetHi, int hEvent, IAsyncResult? ar) : this(offsetLo, offsetHi, new IntPtr(hEvent), ar) { }
    public IAsyncResult AsyncResult { get => _asyncResult!; set => _asyncResult = value; }
    public int OffsetLow { get => _offsetLow; set => _offsetLow = value; }
    public int OffsetHigh { get => _offsetHigh; set => _offsetHigh = value; }
    [Obsolete("Overlapped.EventHandle is not 64-bit compatible and has been deprecated. Use EventHandleIntPtr instead.")]
    public int EventHandle { get => EventHandleIntPtr.ToInt32(); set => EventHandleIntPtr = new IntPtr(value); }
    public IntPtr EventHandleIntPtr { get => _eventHandle; set => _eventHandle = value; }
    [CLSCompliant(false)]
    [Obsolete("This overload is not safe and has been deprecated. Use Pack(IOCompletionCallback?, object?) instead.")]
    public NativeOverlapped* Pack(IOCompletionCallback? iocb) => Pack(iocb, null);
    [CLSCompliant(false)]
    public NativeOverlapped* Pack(IOCompletionCallback? iocb, object? userData) => throw Unsupported();
    [CLSCompliant(false)]
    [Obsolete("This overload is not safe and has been deprecated. Use UnsafePack(IOCompletionCallback?, object?) instead.")]
    public NativeOverlapped* UnsafePack(IOCompletionCallback? iocb) => UnsafePack(iocb, null);
    [CLSCompliant(false)]
    public NativeOverlapped* UnsafePack(IOCompletionCallback? iocb, object? userData) => throw Unsupported();
    [CLSCompliant(false)]
    public static Overlapped Unpack(NativeOverlapped* nativeOverlappedPtr)
    {
        ArgumentNullException.ThrowIfNull(nativeOverlappedPtr);
        throw Unsupported();
    }
    [CLSCompliant(false)]
    public static void Free(NativeOverlapped* nativeOverlappedPtr)
    {
        ArgumentNullException.ThrowIfNull(nativeOverlappedPtr);
        throw Unsupported();
    }
    private static PlatformNotSupportedException Unsupported() => new("Overlapped native I/O packing is not supported on this NetWasm platform.");
}
