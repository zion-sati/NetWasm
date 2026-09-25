#nullable enable
using System;
using System.Runtime.InteropServices.JavaScript;

namespace NetWasm.Fixtures.InstanceLifetime;

public static class Consumer
{
    [JSImport("grow_text", "consumer.memory")]
    public static extern string? GrowText(string? value);

    [JSImport("grow_bytes", "consumer.memory")]
    public static extern byte[]? GrowBytes(byte[]? value);
}

public sealed class State(int value)
{
    public int Value = value;
    public readonly byte[] Bytes = [0, 127, 128, 255];
    public readonly string Text = "retained\0Ω\U0001F31E";
}

public static class EntryPoint
{
    private static State? _state;
    private static int _initializations;

    static EntryPoint() => _initializations++;

    public static int Run(int input) => input;

    [JSExport("initialize_state")]
    public static int Initialize(int value)
    {
        _state = new State(value);
        return _initializations;
    }

    [JSExport("read_state")]
    public static int Read(int _) => _state?.Value ?? -1;

    [JSExport("advance_state")]
    public static int Advance(int delta) => _state!.Value += delta;

    [JSExport("check_text")]
    public static int CheckText(int mode)
    {
        var value = mode switch { 0 => null, 1 => "", _ => "a\0Ω\ud800\U0001F31E" };
        var result = Consumer.GrowText(value);
        return result == (value is null ? null : value + "!") ? 42 : -1;
    }

    [JSExport("check_bytes")]
    public static int CheckBytes(int mode)
    {
        byte[]? value = mode switch { 0 => null, 1 => [], _ => [0, 127, 128, 255] };
        var result = Consumer.GrowBytes(value);
        if (value is null) return result is null ? 42 : -1;
        if (result is null || result.Length != value.Length + 1 || result[^1] != 42) return -2;
        for (var index = 0; index < value.Length; index++)
            if (result[index] != value[index]) return -3;
        return 42;
    }

    [JSExport("collect_and_check")]
    public static int CollectAndCheck(int _)
    {
        GC.Collect();
        var state = _state!;
        return state.Bytes.Length == 4 && state.Bytes[0] == 0 && state.Bytes[1] == 127 &&
            state.Bytes[2] == 128 && state.Bytes[3] == 255 && state.Text == "retained\0Ω\U0001F31E"
            ? 42 : -1;
    }
}
