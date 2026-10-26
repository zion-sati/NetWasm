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

    // System.Threading.Lock (.NET 9): C# 13 lowers lock on a Lock to
    // EnterScope. TUnit's assertion scope uses it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SystemThreadingLockMatchesDesktopOnASingleReactor(bool optimize)
    {
        using var assets = TestAssets.Create();
        const string Source = """
            using System;
            using System.Threading;

            namespace ThreadingLockFixture;

            public static class EntryPoint
            {
                private static readonly Lock Gate = new();

                public static int Run(int input)
                {
                    var total = 0;
                    lock (Gate)
                    {
                        if (!Gate.IsHeldByCurrentThread) return -1;
                        lock (Gate)
                        {
                            total += input;
                        }
                        if (!Gate.IsHeldByCurrentThread) return -2;
                    }
                    if (Gate.IsHeldByCurrentThread) return -3;
                    using (Gate.EnterScope())
                    {
                        if (!Gate.TryEnter() || !Gate.TryEnter(0) || !Gate.TryEnter(TimeSpan.Zero)) return -4;
                        Gate.Exit();
                        Gate.Exit();
                        Gate.Exit();
                    }
                    if (Gate.IsHeldByCurrentThread) return -5;
                    try { Gate.Exit(); return -6; }
                    catch (SynchronizationLockException) { total++; }
                    try { Gate.TryEnter(-2); return -7; }
                    catch (ArgumentOutOfRangeException) { total++; }
                    try { Gate.TryEnter(TimeSpan.FromMilliseconds(-2)); return -8; }
                    catch (ArgumentOutOfRangeException) { total++; }
                    Gate.Enter();
                    Gate.Exit();
                    return Gate.IsHeldByCurrentThread ? -9 : total;
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("ThreadingLockFixture", Source)
            : assets.CompileSource("ThreadingLockFixture", Source);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ThreadingLockFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(44, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
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

    [Fact]
    public void SingleReactorMonitorTracksReferenceIdentityAndRecursion()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "MonitorFixture",
            """
            namespace MonitorFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var first = new EqualReference();
                    var second = new EqualReference();
                    if (System.Threading.Monitor.IsEntered(first)) return -1;

                    System.Threading.Monitor.Enter(first);
                    var firstTaken = false;
                    System.Threading.Monitor.TryEnter(first, ref firstTaken);
                    var secondTaken = false;
                    System.Threading.Monitor.TryEnter(second, System.TimeSpan.Zero, ref secondTaken);
                    if (!firstTaken || !secondTaken ||
                        !System.Threading.Monitor.IsEntered(first) ||
                        !System.Threading.Monitor.IsEntered(second)) return -2;

                    System.Threading.Monitor.Exit(first);
                    if (!System.Threading.Monitor.IsEntered(first)) return -3;
                    System.Threading.Monitor.Exit(first);
                    System.Threading.Monitor.Exit(second);
                    if (System.Threading.Monitor.IsEntered(first) ||
                        System.Threading.Monitor.IsEntered(second)) return -4;

                    try
                    {
                        System.Threading.Monitor.Exit(first);
                        return -5;
                    }
                    catch (System.Threading.SynchronizationLockException)
                    {
                    }

                    try
                    {
                        System.Threading.Monitor.TryEnter(first, -2);
                        return -6;
                    }
                    catch (System.ArgumentOutOfRangeException)
                    {
                    }

                    var alreadyTaken = true;
                    try
                    {
                        System.Threading.Monitor.TryEnter(first, ref alreadyTaken);
                        return -7;
                    }
                    catch (System.ArgumentException)
                    {
                    }
                    return input;
                }

                private sealed class EqualReference
                {
                    public override bool Equals(object? obj) => obj is EqualReference;
                    public override int GetHashCode() => 1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "MonitorFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 42));
    }
}
