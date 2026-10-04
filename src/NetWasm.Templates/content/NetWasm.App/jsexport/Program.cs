using System;
using System.Runtime.InteropServices.JavaScript;

public static partial class WorkerExports
{
    [JSImport("report", "netwasm:worker-template/notifications@1.0.0")]
    private static extern void Report(int completed, int total);

    [JSExport("run")]
    public static int Run(int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        for (var completed = 0; completed < total;)
        {
            completed++;
            Report(completed, total);
        }
        return total;
    }
}
