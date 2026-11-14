using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class RuntimeHelpersCompatibilityCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesRuntimeHelperCompatibilityContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "RuntimeHelpersCompatibilityFixture",
            Source,
            "RuntimeHelpersCompatibilityFixture.EntryPoint",
            optimize,
            target,
            1,
            []));

        Assert.Equal(1, result);
    }

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace RuntimeHelpersCompatibilityFixture;

        public static class EntryPoint
        {
            private static int state;

            private static void RunCode(object? userData)
            {
                if ((int)userData! == 0) throw new InvalidOperationException();
            }

            private static void CleanupCode(object? userData, bool exceptionThrown)
            {
                state = exceptionThrown ? 2 : 1;
            }

            public static int Run(int input)
            {
                state = 0;
                RuntimeHelpers.PrepareConstrainedRegions();
                RuntimeHelpers.PrepareConstrainedRegionsNoOP();
                RuntimeHelpers.PrepareContractedDelegate(RunCode);
                RuntimeHelpers.ProbeForSufficientStack();
                RuntimeHelpers.EnsureSufficientExecutionStack();
                if (!RuntimeHelpers.TryEnsureSufficientExecutionStack()) return -4;
                if (!RuntimeHelpers.Equals(RuntimeHelpers.GetObjectValue(input), input)) return -3;
                try
                {
                    RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup(
                        RunCode,
                        CleanupCode,
                        input);
                    return state;
                }
                catch
                {
                    return -state;
                }
            }
        }
        """;
}
