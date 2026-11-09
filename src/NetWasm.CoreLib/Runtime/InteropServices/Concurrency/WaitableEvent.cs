namespace System.Runtime.InteropServices.Concurrency;

// One event from waitable-set.wait: its code, the waitable it belongs to, and
// the code-specific payload (a copy's progress, a subtask's state).
internal readonly struct WaitableEvent
{
    internal WaitableEvent(int code, int waitable, uint payload)
    {
        Code = code;
        Waitable = waitable;
        Payload = payload;
    }

    internal int Code { get; }

    internal int Waitable { get; }

    internal uint Payload { get; }
}
