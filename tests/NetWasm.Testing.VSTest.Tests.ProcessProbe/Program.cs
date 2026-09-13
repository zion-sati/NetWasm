using System.Globalization;

namespace NetWasm.Testing.VSTest.Tests.ProcessProbe;

public static class ProbeMarker
{
}

public static class Program
{
    public static int Main(string[] arguments)
    {
        if (arguments is ["wait"])
        {
            using var wait = new ManualResetEventSlim();
            wait.Wait();
            return 0;
        }
        if (arguments is not ["emit", var exitCode])
        {
            return 90;
        }

        Console.Out.Write(Environment.GetEnvironmentVariable("NETWASM_VSTEST_KEEP") ?? "<missing>");
        Console.Error.Write(Environment.GetEnvironmentVariable("NETWASM_VSTEST_REMOVE") ?? "<missing>");
        return int.Parse(exitCode, CultureInfo.InvariantCulture);
    }
}
