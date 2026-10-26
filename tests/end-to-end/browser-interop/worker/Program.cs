using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

public static class WorkerExports
{
    private static int _counter;

    [JSImport("report", "app.host")]
    private static extern void Report(int value);

    // Nothing calls this, so trimming removes it and its module is left out.
    [JSImport("optionalFeature", "app.optional")]
    internal static extern string OptionalFeature();

    [JSExport("add")]
    public static int Add(int value) => _counter += value;

    [JSExport("task")]
    public static async Task<int> TaskResult(int value)
    {
        var survivor = new byte[4096];
        survivor[0] = (byte)value;
        survivor[^1] = (byte)(value + 1);
        await Task.Yield();
        var collections = GC.CollectionCount(0);
        GC.Collect();
        if (GC.CollectionCount(0) <= collections || survivor[0] != (byte)value
            || survivor[^1] != (byte)(value + 1))
        {
            throw new InvalidOperationException("Worker GC survivor contract failed.");
        }
        Report(value);
        return value + 1;
    }

    [JSExport("valueTask")]
    public static async ValueTask<int> ValueTaskResult(int value)
    {
        await Task.Yield();
        return value + 2;
    }

    [JSExport("fail")]
    public static async Task<int> Fail()
    {
        await Task.Yield();
        throw new FormatException("worker-probe");
    }

    [JSExport("failTask")]
    public static async Task FailTask(int messageKind)
    {
        await Task.Yield();
        throw new FormatException(Message(messageKind));
    }

    [JSExport("failValueTask")]
    public static async ValueTask FailValueTask(int messageKind)
    {
        await Task.Yield();
        throw new FormatException(Message(messageKind));
    }

    [JSExport("failValueTaskResult")]
    public static async ValueTask<int> FailValueTaskResult(int messageKind)
    {
        await Task.Yield();
        throw new FormatException(Message(messageKind));
    }

    private static string? Message(int kind) => kind switch
    {
        0 => "",
        1 => "Unicode Ω 😀",
        2 => "call-specific",
        3 => "first instance",
        4 => "second instance",
        _ => null,
    };

    [JSExport("pending")]
    public static Task<int> Pending() => new TaskCompletionSource<int>().Task;

    private static TaskCompletionSource<int>? _gate;
    private static bool _stop;

    // Issue #126: page code stops a running job by calling another export,
    // and the job returns how far it got.
    [JSExport("countUntilStopped")]
    public static async Task<int> CountUntilStopped()
    {
        _stop = false;
        var count = 0;
        while (!_stop)
        {
            count++;
            await Task.Delay(1);
        }
        return count;
    }

    [JSExport("stop")]
    public static void Stop() => _stop = true;

    // Issue #126: a call runs while another awaits, as on the page; serialized
    // calls would leave this waiting for a release that never starts.
    [JSExport("waitForRelease")]
    public static async Task<int> WaitForRelease()
    {
        _gate = new TaskCompletionSource<int>();
        return await _gate.Task + 1;
    }

    [JSExport("release")]
    public static void Release(int value) => _gate!.SetResult(value);

    // Issue #127: an async export settles with what a synchronous one returns.
    [JSExport("describe")]
    public static async Task<string> Describe(string name)
    {
        await Task.Yield();
        return "worker:" + name;
    }

    [JSExport("halves")]
    public static async ValueTask<double[]> Halves(int[] values)
    {
        await Task.Yield();
        var halves = new double[values.Length];
        for (var index = 0; index < values.Length; index++) halves[index] = values[index] / 2.0;
        return halves;
    }
}
