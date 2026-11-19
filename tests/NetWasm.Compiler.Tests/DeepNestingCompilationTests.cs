// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// The compiler's passes recurse once per nesting level of the method they work on: structuring an else-if chain
// whose arms rejoin at the code after it recursed once per arm. On a thread pool thread's stack, 3,000 arms
// crashed the compiler at every level (and 0.15's run-time Type attribute queries build such chains, one arm per
// candidate type). The compiler's workers now run on large-stack threads, so the chain compiles and runs as on
// desktop .NET.
public sealed class DeepNestingCompilationTests
{
    private const int Arms = 3000;

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void AnElseIfChainOfThousandsOfArmsCompilesAndRuns(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            "DeepNestingFixture",
            Source(),
            "DeepNestingFixture.EntryPoint",
            optimize,
            target,
            Arms,
            []));

        Assert.Equal(Expected(), observed);
    }

    // Sums Classify over every arm and a few values past them, as desktop .NET computes it.
    private static int Expected()
    {
        var sum = 0;
        for (var value = 0; value < Arms + 5; value++)
        {
            sum += Classify(value);
        }
        return sum;

        static int Classify(int value) => (value < Arms ? value * 7 % 101 : -1) * 2 + 1;
    }

    private static string Source()
    {
        var source = new StringBuilder("""
            namespace DeepNestingFixture;

            public static class EntryPoint
            {
                public static int Run(int arms)
                {
                    var sum = 0;
                    for (var value = 0; value < arms + 5; value++)
                    {
                        sum += Classify(value);
                    }
                    return sum;
                }

                private static int Classify(int value)
                {
                    int result;
                    if (value == 0) result = 0;

            """);
        for (var arm = 1; arm < Arms; arm++)
        {
            source.AppendLine(CultureInfo.InvariantCulture, $"        else if (value == {arm}) result = {arm * 7 % 101};");
        }
        source.Append("""
                    else result = -1;
                    return result * 2 + 1;
                }
            }
            """);
        return source.ToString();
    }
}
