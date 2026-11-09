namespace System.Runtime.InteropServices.Concurrency;

internal interface ISubtaskReturnAwaiter
{
    // Finishes an async-lowered call from the status it returned: waits until
    // the callee has returned, then releases the subtask.
    void AwaitReturn(int status);
}
