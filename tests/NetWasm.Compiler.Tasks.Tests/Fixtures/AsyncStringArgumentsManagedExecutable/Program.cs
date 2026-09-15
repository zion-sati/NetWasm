namespace NetWasm.Compiler.Tasks.Tests.Fixtures.AsyncStringArgumentsManagedExecutable;

internal static class Program
{
    public static async Task<int> Main(string[] arguments)
    {
        await Task.Yield();
        return arguments.Length;
    }
}

public sealed class Marker;
