namespace System.Runtime.InteropServices.Concurrency;

internal interface IComponentBackpressure
{
    // Holding backpressure keeps the host from starting another async export task.
    void Hold(bool held);
}
