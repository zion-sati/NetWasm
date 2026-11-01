using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class DelegateDynamicInvokeCompilationTests
{
    [Fact]
    public void UnreachableDynamicInvokeDoesNotChangeHello42Output()
    {
        using var assets = TestAssets.Create();
        const string baselineSource =
            "public static class EntryPoint { public static int Run(int input) => input + 1; }";
        const string unusedSource =
            """
            using System;

            public static class EntryPoint
            {
                public static int Run(int input) => input + 1;

                private static object? Unused(Delegate callback, object?[]? arguments) =>
                    callback.DynamicInvoke(arguments);
            }
            """;
        var output = assets.CompileOptimizedSource("DynamicInvokePayUse", baselineSource);
        var baselineAssembly = Path.Combine(assets.Directory, "DynamicInvokePayUse.baseline.dll");
        File.Copy(output, baselineAssembly);
        var baseline = NetWasmCompiler.Compile(new(
            baselineAssembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));
        var unusedAssembly = assets.CompileOptimizedSource("DynamicInvokePayUse", unusedSource);
        var unused = NetWasmCompiler.Compile(new(
            unusedAssembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));

        Assert.Equal(baseline.ApplicationModule, unused.ApplicationModule);
        Assert.Null(baseline.Program.MemberExecution.DelegateInvocation);
        Assert.Null(unused.Program.MemberExecution.DelegateInvocation);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DynamicInvokePreservesTypedDelegateSemantics(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"DelegateDynamicInvoke{optimize}{target}",
            """
            using System;
            using System.Reflection;

            public class Base
            {
                public int Value;
            }

            public sealed class Derived : Base
            {
            }

            public struct Pair
            {
                public int Number;
                public object? Reference;
            }

            public delegate Pair Transform<T>(
                T prefix,
                Pair value,
                ref int number,
                out string text);

            public static class EntryPoint
            {
                private static int _calls;

                public static int Run(int input)
                {
                    _calls = 0;
                    Action empty = () => _calls++;
                    if (empty.DynamicInvoke(null) is not null || _calls != 1) return -1;
                    if (empty.DynamicInvoke(Array.Empty<object?>()) is not null || _calls != 2)
                        return -2;

                    Func<int, int> identity = value => value;
                    if ((int)identity.DynamicInvoke(input)! != input) return -3;
                    if ((int)identity.DynamicInvoke(new object?[] { null })! != 0) return -4;

                    var marker = new object();
                    Transform<string> transform = (string prefix, Pair value,
                        ref int number, out string text) =>
                    {
                        number += value.Number;
                        text = prefix + number.ToString();
                        return new Pair { Number = number, Reference = value.Reference };
                    };
                    object?[] arguments =
                    [
                        "n",
                        new Pair { Number = 3, Reference = marker },
                        input,
                        null,
                    ];
                    var pair = (Pair)transform.DynamicInvoke(arguments)!;
                    if (pair.Number != input + 3 || !ReferenceEquals(pair.Reference, marker))
                        return -5;
                    if ((int)arguments[2]! != input + 3 || (string)arguments[3]! != "n" + (input + 3).ToString())
                        return -6;

                    Func<int, int> first = value => { _calls += 10; return value + 1; };
                    Func<int, int> second = value => { _calls += 100; return value + 2; };
                    if ((int)(first + second).DynamicInvoke(input)! != input + 2 || _calls != 112)
                        return -7;

                    Func<int, int?> nullable = value => value == 0 ? null : value + 4;
                    if (nullable.DynamicInvoke(0) is not null) return -8;
                    if ((int)nullable.DynamicInvoke(input)! != input + 4) return -9;

                    Func<int, object> referenceResult = value =>
                        new Derived { Value = value + 5 };
                    if (((Derived)referenceResult.DynamicInvoke(input)!).Value != input + 5)
                        return -16;
                    Func<float, float> single = value => value + 0.5F;
                    if ((float)single.DynamicInvoke(1.25F)! != 1.75F) return -17;
                    Func<double, double> real = value => value + 0.25;
                    if ((double)real.DynamicInvoke(1.5)! != 1.75) return -18;
                    Func<nint, nint> pointer = value => value + 3;
                    if ((nint)pointer.DynamicInvoke((nint)39)! != 42) return -19;

                    Func<Base, int> reference = value => value.Value;
                    if ((int)reference.DynamicInvoke(new Derived { Value = input })! != input)
                        return -10;
                    try { reference.DynamicInvoke(new object()); return -11; }
                    catch (ArgumentException) { }
                    try { identity.DynamicInvoke(); return -12; }
                    catch (TargetParameterCountException) { }
                    try { identity.DynamicInvoke("wrong"); return -13; }
                    catch (ArgumentException) { }
                    try { new Func<int>(Throw).DynamicInvoke(); return -14; }
                    catch (TargetInvocationException error)
                    {
                        if (error.InnerException is not InvalidOperationException) return -15;
                    }
                    return input + 1;
                }

                private static int Throw() =>
                    throw new InvalidOperationException("dynamic target");
            }
            """,
            "EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(42, result);
    }
}
