namespace System.Runtime.InteropServices.Concurrency;

internal interface IWasiSubtaskCancelInvoker
{
    int Cancel(int subtask);
}
