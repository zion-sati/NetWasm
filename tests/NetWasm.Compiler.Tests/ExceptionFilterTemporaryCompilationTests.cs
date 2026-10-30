using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A filter funclet declares its own locals. Each filter here reaches an emitter
// that uses a scratch temporary beyond the I4 and I8 ones: a type test against a
// set of type ids, and, once inlined at O3, the enum, Array.GetValue, delegate
// DynamicInvoke and value-type hash code intrinsics. The funclet used to leave
// those temporaries undeclared or give them the I8 local, so it failed validation.
public sealed class ExceptionFilterTemporaryCompilationTests
{
    public static TheoryData<bool, WasmTarget, FinalWasmOptimization> Cases()
    {
        var cases = new TheoryData<bool, WasmTarget, FinalWasmOptimization>();
        foreach (var optimization in new[] { FinalWasmOptimization.Oz, FinalWasmOptimization.O3 })
        {
            foreach (var optimize in new[] { false, true })
            {
                foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                {
                    cases.Add(optimize, target, optimization);
                }
            }
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void FiltersUseEveryScratchTemporary(
        bool optimize,
        WasmTarget target,
        FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"FilterTemporaries{optimization}{(optimize ? "Release" : "Debug")}{target}",
            Source,
            "FilterTemporaries.EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            Optimization = optimization,
        });

        Assert.Equal(42, result);
    }

    private const string Source = """
        using System;
        namespace FilterTemporaries;

        public enum Color { Red, Green, Blue }

        public struct Point
        {
            public int X;
            public int Y;
        }

        public static class EntryPoint
        {
            static readonly int[] Numbers = { 1, 5, 9 };
            static readonly Func<int, int> Twice = value => value * 2;

            static void Fail(Exception exception) => throw exception;

            public static int Run(int input)
            {
                var hash = new Point { X = input, Y = 2 }.GetHashCode();
                var passed = 0;
                try { Fail(new InvalidOperationException("outer", new ArgumentException("inner"))); }
                catch (InvalidOperationException e) when (e.InnerException is ArgumentException) { passed |= 1; }
                try { Fail(new InvalidOperationException()); }
                catch (Exception) when (Enum.GetValues<Color>().Length == 3) { passed |= 2; }
                try { Fail(new InvalidOperationException()); }
                catch (Exception) when (Enum.GetNames<Color>()[2] == "Blue") { passed |= 4; }
                try { Fail(new InvalidOperationException()); }
                catch (Exception) when ((int)((Array)Numbers).GetValue(1)! == 5) { passed |= 8; }
                try { Fail(new InvalidOperationException()); }
                catch (Exception) when ((int)Twice.DynamicInvoke(input)! == input * 2) { passed |= 16; }
                try { Fail(new InvalidOperationException()); }
                catch (Exception) when (new Point { X = input, Y = 2 }.GetHashCode() == hash) { passed |= 32; }
                try
                {
                    try { Fail(new InvalidOperationException()); }
                    catch (Exception e) when (e is ArgumentException || Enum.GetNames<Color>().Length == 4) { return 100; }
                }
                catch (InvalidOperationException) { passed |= 64; }
                return passed == 127 ? input + 1 : 200 + passed;
            }
        }
        """;
}
