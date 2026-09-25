using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class LockCompilationTests
{
    [Fact]
    public void CompilerLowersSingleThreadedNestedLockAndFinallyCleanup()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "LockFixture",
            """
            namespace LockFixture;

            public static class EntryPoint
            {
                private static readonly object Gate = new();

                public static int Run(int input)
                {
                    var total = 0;
                    lock (Gate)
                    {
                        total += input;
                        lock (Gate)
                        {
                            total++;
                        }
                    }
                    try
                    {
                        lock ((object)null!)
                        {
                        }
                    }
                    catch (System.ArgumentNullException)
                    {
                        total++;
                    }
                    return total;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "LockFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(43, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void BlockingMonitorOperationsArePlatformUnsupported()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "BlockingMonitorFixture",
            """
            namespace BlockingMonitorFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    try
                    {
                        System.Threading.Monitor.Wait(new object());
                    }
                    catch (System.PlatformNotSupportedException)
                    {
                        return input + 1;
                    }
                    return 0;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "BlockingMonitorFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }
}
