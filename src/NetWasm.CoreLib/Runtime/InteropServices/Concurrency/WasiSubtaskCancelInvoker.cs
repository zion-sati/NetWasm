namespace System.Runtime.InteropServices.Concurrency;

internal sealed class WasiSubtaskCancelInvoker : IWasiSubtaskCancelInvoker
{
    public int Cancel(int subtask) => WaitableSetImports.CancelSubtask(subtask);
}
