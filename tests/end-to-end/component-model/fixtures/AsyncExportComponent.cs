using System.Threading.Tasks;

namespace NetWasm.Wit.Netwasm.Test.Async._1._0._0;

public static partial class AsyncExportExports
{
    // Awaits Task.Delay and the generated wait-for binding, then reports whether the monotonic
    // clock saw at least the requested delay.
    public static async partial Task<string> Greet(string name, uint milliseconds)
    {
        var before = MonotonicClockImports.Now();
        await Task.Delay((int)milliseconds);
        await MonotonicClockImports.WaitFor(1_000_000);
        var waited = (MonotonicClockImports.Now() - before) / 1_000_000 >= milliseconds;
        return $"hello {name}, waited {waited}";
    }

    // The function entry nwc compiles a library from.
    public static int Run(int value) => value;
}
