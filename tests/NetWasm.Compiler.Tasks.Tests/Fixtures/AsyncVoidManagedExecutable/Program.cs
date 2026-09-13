namespace NetWasm.Compiler.Tasks.Tests.Fixtures.AsyncVoidManagedExecutable;

public static class Program
{
    public static async Task Main(string[] arguments)
    {
        await Task.Yield();
        _ = arguments.Length;
    }
}

public sealed class Marker;
