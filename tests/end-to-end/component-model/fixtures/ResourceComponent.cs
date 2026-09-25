using System;

namespace NetWasm.Wit.Netwasm.Test.Resource._1._0._0;

public static partial class TestResourceExports
{
    public static partial Counter CreateCounter(uint initial) =>
        Counter.Create(new CounterState(initial));

    public static partial uint Get(Counter self) =>
        ((CounterState)self.State).Value;

    public static partial void Add(Counter self, uint delta)
    {
        var state = (CounterState)self.State;
        state.Value = checked(state.Value + delta);
    }

    public static partial uint Inspect(Counter value) =>
        ((CounterState)value.State).Value;

    public static partial uint Consume(Counter value)
    {
        using (value)
        {
            return ((CounterState)value.State).Value;
        }
    }

    public static partial Counter Make(uint initial) =>
        Counter.Create(new CounterState(initial));

    public static partial uint ExerciseHost(uint initial)
    {
        var result = 0u;
        using (var value = HostHandlesImports.CreateHostCounter(initial))
        {
            if (HostHandlesImports.Get(value) == initial) result |= 1;
            HostHandlesImports.Add(value, 2);
            if (HostHandlesImports.Inspect(value) == initial + 2) result |= 2;
        }

        var consumed = HostHandlesImports.Make(initial + 3);
        if (HostHandlesImports.Consume(consumed) == initial + 3) result |= 4;
        try
        {
            HostHandlesImports.Get(consumed);
        }
        catch (InvalidOperationException)
        {
            result |= 8;
        }
        return result;
    }

    public static partial uint GuestResourceLive() => CounterState.Live;

    public static partial uint GuestResourceDisposed() => CounterState.Disposed;

    private sealed class CounterState : IDisposable
    {
        public CounterState(uint value)
        {
            Value = value;
            Live++;
        }

        public static uint Live { get; private set; }

        public static uint Disposed { get; private set; }

        public uint Value { get; set; }

        public void Dispose()
        {
            Live--;
            Disposed++;
            Value = uint.MaxValue;
        }
    }
}

public static class ResourceComponent
{
    public static int Run(int value) => value;
}
