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

    [JSImport("resolve_text", "consumer.promises")]
    public static extern Task<string?> ResolveText(string value);

    [JSImport("resolve_doubles", "consumer.promises")]
    public static extern Task<double[]?> ResolveDoubles(int[] values);

    [JSImport("resolve_object", "consumer.promises")]
    public static extern ValueTask<JSObject?> ResolveObject(int value);

    [JSImport("object_value", "consumer.promises")]
    public static extern int ObjectValue(JSObject value);

    [JSImport("reenter", "consumer.promises")]
    public static extern int Reenter(int depth);
}

public struct LiveFrame
{
    public string Text;
    public int[] Values;
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

    [JSExport("reference_round_trip")]
    public static async Task<int> ReferenceRoundTrip(string name, int[] values)
    {
        var text = await HostPromises.ResolveText(name);
        var doubles = await HostPromises.ResolveDoubles(values);
        using var host = await HostPromises.ResolveObject(values.Length);
        var total = 0.0;
        foreach (var value in doubles!)
        {
            total += value;
        }
        return text!.Length * 1000 + (int)total + HostPromises.ObjectValue(host!);
    }

    // Issue #127: an asynchronous export settles with the values a synchronous one returns.
    [JSExport("text_later")]
    public static async Task<string?> TextLater(string name) => await HostPromises.ResolveText(name);

    [JSExport("doubles_later")]
    public static async ValueTask<double[]?> DoublesLater(int[] values) =>
        await HostPromises.ResolveDoubles(values);

    [JSExport("object_later")]
    public static async Task<JSObject?> ObjectLater(int value) => await HostPromises.ResolveObject(value);

    [JSExport("echo_object")]
    public static JSObject? EchoObject(JSObject? value) => value;

    [JSExport("read_object")]
    public static int ReadObject(JSObject value)
    {
        using (value)
        {
            return HostPromises.ObjectValue(value);
        }
    }

    [JSExport("read_object_later")]
    public static async Task<int> ReadObjectLater(JSObject value)
    {
        await HostPromises.Signal(17);
        using (value)
        {
            return HostPromises.ObjectValue(value) + 1;
        }
    }

    // Each level keeps managed references live across a synchronous call back
    // into this export and collects inside it. Nest(d) is (d + 1) squared.
    [JSExport("nest")]
    public static int Nest(int depth)
    {
        var frame = new LiveFrame { Text = new string('x', depth + 1), Values = [depth, depth * 2] };
        int inner;
        try
        {
            inner = depth == 0 ? 0 : HostPromises.Reenter(depth - 1);
        }
        catch (JSException)
        {
            return -1;
        }
        GC.Collect();
        return inner < 0 ? inner : inner + frame.Text.Length + frame.Values[1] - frame.Values[0];
    }

    [JSExport("null_references")]
    public static async Task<int> NullReferences(string? name)
    {
        var text = await HostPromises.ResolveText(name!);
        var doubles = await HostPromises.ResolveDoubles([]);
        var host = await HostPromises.ResolveObject(-1);
        return (name is null ? 1 : 0) + (text is null ? 2 : 0) + (doubles is null ? 4 : 0) + (host is null ? 8 : 0);
    }
}
