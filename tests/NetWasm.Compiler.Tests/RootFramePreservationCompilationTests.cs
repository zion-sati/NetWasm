using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class RootFramePreservationCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void LiveReferenceSurvivesHelperCallAndSubsequentAllocation(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "RootFramePreservation",
            ObjectSource,
            "RootFramePreservation.Program",
            optimize,
            target,
            0,
            []));

        Assert.Equal(0, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void LiveStringSurvivesGuardCallAndSecondMaterialization(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "StringRootFramePreservation",
            StringSource,
            "StringRootFramePreservation.Program",
            optimize,
            target,
            0,
            []));

        Assert.Equal(0, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void InstanceDelegatePreservesStringSemantics(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "DelegateStringPreservation",
            DelegateStringSource,
            "DelegateStringPreservation.Program",
            optimize,
            target,
            0,
            []));

        Assert.Equal(0, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void InstanceDelegatePreservesUnsignedScalarReturn(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "DelegateUnsignedScalar",
            DelegateUnsignedScalarSource,
            "DelegateUnsignedScalar.Program",
            optimize,
            target,
            0,
            []));

        Assert.Equal(0, observed);
    }

    private const string ObjectSource = """
        namespace RootFramePreservation;

        public static class Program
        {
            public static int Run(int _)
            {
                var first = new Holder(17);
                var observed = Observe(first);
                var second = new Holder(23);
                System.GC.Collect();

                return observed == 17 && first.Value == 17 && second.Value == 23 ? 0 : 1;
            }

            private static int Observe(Holder holder) => holder.Value;

            private sealed class Holder
            {
                public Holder(int value)
                {
                    Value = value;
                }

                public int Value { get; }
            }
        }
        """;

    private const string StringSource = """
        namespace StringRootFramePreservation;

        public static class Program
        {
            public static int Run(int _)
            {
                var first = System.Convert.ToHexString(CreateFirstBytes());
                Require(first.ToLowerInvariant() == "abcdef12");
                var second = System.Convert.ToHexString(CreateSecondBytes());

                return first == "ABCDEF12" && second == "10203040" ? 0 : 1;
            }

            private static byte[] CreateFirstBytes() => [0xab, 0xcd, 0xef, 0x12];

            private static byte[] CreateSecondBytes() => [0x10, 0x20, 0x30, 0x40];

            private static void Require(bool condition)
            {
                if (!condition)
                {
                    throw new System.InvalidOperationException();
                }
            }
        }
        """;

    private const string DelegateStringSource = """
        namespace DelegateStringPreservation;

        public static class Program
        {
            public static int Run(int _)
            {
                var probe = new Probe();
                System.Action action = probe.Execute;

                try
                {
                    action();
                    return 0;
                }
                catch
                {
                    return 1;
                }
            }

            private sealed class Probe
            {
                public void Execute()
                {
                    var first = System.Convert.ToHexString(CreateFirstBytes());
                    Require(first.ToLowerInvariant() == "abcdef12");
                    var second = System.Convert.ToHexString(CreateSecondBytes());

                    Require(first == "ABCDEF12" && second == "10203040");
                }

                private static byte[] CreateFirstBytes() => [0xab, 0xcd, 0xef, 0x12];

                private static byte[] CreateSecondBytes() => [0x10, 0x20, 0x30, 0x40];

                private static void Require(bool condition)
                {
                    if (!condition)
                    {
                        throw new System.InvalidOperationException();
                    }
                }
            }
        }
        """;

    private const string DelegateUnsignedScalarSource = """
        namespace DelegateUnsignedScalar;

        public static class Program
        {
            public static int Run(int _)
            {
                var probe = new Probe();
                var direct = probe.Calculate();
                System.Func<uint> calculate = probe.Calculate;

                return calculate() == direct ? 0 : 1;
            }

            private sealed class Probe
            {
                public uint Calculate() => 0xf1234567U;
            }
        }
        """;
}
