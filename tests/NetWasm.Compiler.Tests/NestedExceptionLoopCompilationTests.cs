using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NestedExceptionLoopCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerExecutesNestedExceptionRegionsInsideLoops(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;

            namespace NestedExceptionLoopFixture;

            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    if (NormalCatchFinally(value) != value + 6)
                    {
                        return 1;
                    }

                    if (HandledCatchFinally(value) != value + 9)
                    {
                        return 2;
                    }

                    if (ContinueFromNestedCatch(value) != value + 8)
                    {
                        return 3;
                    }

                    if (BreakFromNestedCatch(value) != value + 5)
                    {
                        return 4;
                    }

                    if (FilteredCatchFinally(value) != value + 13)
                    {
                        return 5;
                    }

                    if (NestedFinally(value) != value + 14)
                    {
                        return 6;
                    }

                    if (NestedHandlerLoop(value) != value + 6)
                    {
                        return 7;
                    }

                    return 0;
                }

                private static int NormalCatchFinally(int value)
                {
                    var remaining = 2;
                    while (remaining-- > 0)
                    {
                        try
                        {
                            value++;
                        }
                        catch (Exception)
                        {
                            value += 4;
                        }
                        finally
                        {
                            value += 2;
                        }
                    }

                    return value;
                }

                private static int HandledCatchFinally(int value)
                {
                    var remaining = 2;
                    while (remaining-- > 0)
                    {
                        try
                        {
                            if (remaining == 1)
                            {
                                throw new InvalidOperationException();
                            }

                            value++;
                        }
                        catch (InvalidOperationException)
                        {
                            value += 4;
                        }
                        finally
                        {
                            value += 2;
                        }
                    }

                    return value;
                }

                private static int ContinueFromNestedCatch(int value)
                {
                    var remaining = 3;
                    while (remaining-- > 0)
                    {
                        try
                        {
                            if (remaining == 1)
                            {
                                continue;
                            }

                            value++;
                        }
                        catch (Exception)
                        {
                            value += 100;
                        }
                        finally
                        {
                            value += 2;
                        }
                    }

                    return value;
                }

                private static int BreakFromNestedCatch(int value)
                {
                    var remaining = 3;
                    while (remaining-- > 0)
                    {
                        try
                        {
                            if (remaining == 1)
                            {
                                break;
                            }

                            value++;
                        }
                        catch (Exception)
                        {
                            value += 100;
                        }
                        finally
                        {
                            value += 2;
                        }
                    }

                    return value;
                }

                private static int FilteredCatchFinally(int value)
                {
                    var remaining = 2;
                    while (remaining-- > 0)
                    {
                        try
                        {
                            if (remaining == 1)
                            {
                                throw new InvalidOperationException();
                            }

                            value++;
                        }
                        catch (InvalidOperationException) when (remaining == 1)
                        {
                            value += 8;
                        }
                        finally
                        {
                            value += 2;
                        }
                    }

                    return value;
                }

                private static int NestedFinally(int value)
                {
                    var remaining = 2;
                    while (remaining-- > 0)
                    {
                        try
                        {
                            try
                            {
                                value++;
                            }
                            finally
                            {
                                value += 4;
                            }
                        }
                        finally
                        {
                            value += 2;
                        }
                    }

                    return value;
                }

                private static int NestedHandlerLoop(int value)
                {
                    try
                    {
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException)
                    {
                        var remaining = 2;
                        while (remaining-- > 0)
                        {
                            try
                            {
                                value++;
                            }
                            finally
                            {
                                value += 2;
                            }
                        }
                    }

                    return value;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            optimize ? "NestedExceptionLoopOptimized" : "NestedExceptionLoopDebug",
            source,
            "NestedExceptionLoopFixture.EntryPoint",
            optimize,
            target,
            10,
            []));

        Assert.Equal(0, observed);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    [Trait("Issue", "63")]
    public void CompilerExecutesLoopWhoseHeaderStartsAnExceptionRegion(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;

            namespace ExceptionLoopEntryFixture;

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int value)
                {
                    _trace = value;
                    if (Execute(shouldThrow: false) != value + 101)
                    {
                        return 1;
                    }

                    try
                    {
                        Execute(shouldThrow: true);
                        return 2;
                    }
                    catch (InvalidOperationException)
                    {
                        return _trace == value + 212 ? 0 : 3;
                    }
                }

                private static int Execute(bool shouldThrow)
                {
                    var depth = 0;
                    try
                    {
                        do
                        {
                            Step(ref depth, shouldThrow);
                        }
                        while (depth > 0);
                    }
                    catch
                    {
                        _trace += 10;
                        throw;
                    }
                    finally
                    {
                        _trace += 100;
                    }

                    return _trace;
                }

                private static void Step(ref int depth, bool shouldThrow)
                {
                    _trace++;
                    if (shouldThrow)
                    {
                        throw new InvalidOperationException();
                    }
                    depth--;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            optimize ? "ExceptionLoopEntryOptimized" : "ExceptionLoopEntryDebug",
            source,
            "ExceptionLoopEntryFixture.EntryPoint",
            optimize,
            target,
            10,
            []));

        Assert.Equal(0, observed);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerExecutesCancellationCallbacksThatUseNestedExceptionLoops(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            namespace CancellationCallbackFixture;

            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    using var source = new CancellationTokenSource();
                    var pending = Task.Delay(1, source.Token);
                    source.Cancel();
                    return pending.IsCanceled ? value : -value;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            optimize ? "CancellationCallbackOptimized" : "CancellationCallbackDebug",
            source,
            "CancellationCallbackFixture.EntryPoint",
            optimize,
            target,
            42,
            [])
        {
            Exports =
            [
                new RequestedExport(
                    "run",
                    "CancellationCallbackFixture.EntryPoint",
                    "Run"),
            ],
            WitPath = Path.Combine(
                assets.Root,
                "wit",
                "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/async-platform@1.0.0",
        });

        Assert.Equal(42, observed);
    }
}
