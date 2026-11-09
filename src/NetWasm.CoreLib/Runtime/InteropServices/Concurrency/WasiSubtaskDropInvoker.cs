namespace System.Runtime.InteropServices.Concurrency;

internal sealed class WasiSubtaskDropInvoker : IWasiSubtaskDropInvoker
{
    public void Drop(int subtask) => WaitableSetImports.DropSubtask(subtask);
}
