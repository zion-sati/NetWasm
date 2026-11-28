// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ValueTaskCompilationTests
{
    [Fact]
    public void CompilerCompilesCompletedAndTaskBackedValueTasks()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ValueTaskFixture",
            """
            using System.Threading.Tasks;

            namespace ValueTaskFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var completed = new ValueTask<int>(input);
                    var backed = new ValueTask<int>(Task<int>.FromResult(1));
                    var empty = default(ValueTask);
                    return completed.Result + backed.Result
                        + (completed.IsCompletedSuccessfully ? 1 : 0)
                        + (backed.AsTask().Result == 1 ? 1 : 0)
                        + (empty.IsCompletedSuccessfully ? 1 : 0);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(CreateReactorOptions(
            assets,
            assembly,
            "ValueTaskFixture.EntryPoint",
            "Run"));

        Assert.Contains(result.Program.ConstructedTypes, type =>
            type.CanonicalName.Contains("ValueTask`1", StringComparison.Ordinal));
        Assert.NotEmpty(result.ApplicationModule);
    }

    [Fact]
    public void CompilerCompilesAsyncValueTaskBuilders()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "AsyncValueTaskFixture",
            """
            using System.Threading.Tasks;

            namespace AsyncValueTaskFixture;

            // A struct's async methods keep their state machines (a class's compile as runtime async).
            public struct Pending
            {
                public static async ValueTask<int> AddAsync(int value)
                {
                    await Task.Yield();
                    return value + 1;
                }

                public static async ValueTask CompleteAsync()
                {
                    await Task.Yield();
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    _ = Pending.AddAsync(input);
                    _ = Pending.CompleteAsync();
                    return input;
                }
            }
            """);

        // Both builders complete their tasks. Inlining is off: Create only returns a default builder, and each
        // builder's SetResult is a small method of a struct, which the loops policy inlines where it is called.
        var result = NetWasmCompiler.Compile(CreateReactorOptions(
            assets,
            assembly,
            "AsyncValueTaskFixture.EntryPoint",
            "Run") with
        {
            OptimizationOverrides = new(ManagedMethodInlining: ManagedMethodInlining.Disabled),
        });

        Assert.Contains(result.Program.MethodInstances.Values, method =>
            method.Definition.Name == "SetResult" &&
            method.DeclaringType.CanonicalName.Contains("AsyncValueTaskMethodBuilder`1", StringComparison.Ordinal));
        Assert.Contains(result.Program.MethodInstances.Values, method =>
            method.Definition.Name == "SetResult" &&
            method.DeclaringType.CanonicalName.EndsWith("System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder",
                StringComparison.Ordinal));
        Assert.NotEmpty(result.ApplicationModule);
    }
}
