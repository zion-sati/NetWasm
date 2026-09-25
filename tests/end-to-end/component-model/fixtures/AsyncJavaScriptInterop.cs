#nullable enable

using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace NetWasm.Fixtures.JavaScriptInterop;

public static class HostPromises
{
    [JSImport("resolve", "consumer.promises")]
    public static extern Task<int> Resolve(int value);

    [JSImport("resolve_value", "consumer.promises")]
    public static extern ValueTask<int> ResolveValue(int value);

    [JSImport("reject", "consumer.promises")]
    public static extern Task<int> Reject(int value);

    [JSImport("never", "consumer.promises")]
    public static extern Task<int> Never(int value);

    [JSImport("signal", "consumer.promises")]
    public static extern Task Signal(int value);

    [JSImport("signal_value", "consumer.promises")]
    public static extern ValueTask SignalValue(int value);
}

public static class EntryPoint
{
    public static int Run(int input) => input;

    [JSExport("round_trip_task")]
    public static async Task<int> RoundTripTask(int input) =>
        await HostPromises.Resolve(input) + 1;

    [JSExport("round_trip_value_task")]
    public static async ValueTask<int> RoundTripValueTask(int input) =>
        await HostPromises.ResolveValue(input) + 2;

    [JSExport("observe_rejection")]
    public static async Task<int> ObserveRejection(int input)
    {
        try
        {
            return await HostPromises.Reject(input);
        }
        catch (JSException)
        {
            return input + 3;
        }
    }

    [JSExport("fault")]
    public static Task<int> Fault(int input) =>
        Task<int>.FromException(new InvalidOperationException());

    [JSExport("cancel")]
    public static Task<int> Cancel(int input)
    {
        var completion = new TaskCompletionSource<int>();
        completion.SetCanceled();
        return completion.Task;
    }

    [JSExport("immediate_value_task")]
    public static ValueTask<int> ImmediateValueTask(int input) => new(input + 4);

    [JSExport("observe_cancellation")]
    public static async Task<int> ObserveCancellation(int input)
    {
        try
        {
            return await HostPromises.Never(input);
        }
        catch (TaskCanceledException)
        {
            return input + 5;
        }
    }

    [JSExport("task_void")]
    public static async Task TaskVoid(int input) =>
        await HostPromises.Signal(input);

    [JSExport("value_task_void")]
    public static async ValueTask ValueTaskVoid(int input) =>
        await HostPromises.SignalValue(input);
}
