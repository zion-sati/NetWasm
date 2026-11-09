namespace System.Runtime.InteropServices.Concurrency;

internal interface IWaitableEventAwaiter
{
    // Blocks until the waitable has an event and returns the event's payload.
    uint Await(int waitable);
}
