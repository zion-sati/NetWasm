namespace NetWasm.Compiler.Tasks.Tests.Fixtures.AsyncManagedExecutable;

public sealed class Program
{
    private readonly int _instanceValue = 1;

    public static async Task<int> Main()
    {
        await Task.Yield();
        return 17;
    }

    public static int Main(int value) => value;

    public static T Main<T>() => default!;

    public static Task<string> Main(double value) =>
        Task.FromResult(value.ToString(
            System.Globalization.CultureInfo.InvariantCulture));

    public static Task<int> Main(long value) =>
        Task.FromResult((int)value);

    public static Program Main(float value) => new();

    public static ValueTask Main(decimal value) => default;

    public Task<int> Main(bool value) =>
        Task.FromResult(value ? _instanceValue : 0);
}

public sealed class Marker;
