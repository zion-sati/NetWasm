namespace System.Runtime.InteropServices.Concurrency;

internal interface IWasiSubtaskDropInvoker
{
    void Drop(int subtask);
}
