using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
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

    // DynamicInvoke on a variable of a delegate type carries Invoke metadata and an invocation stub
    // for that type alone; the program's other delegate types, invoked directly, carry none.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DynamicInvokeCarriesInvokeMetadataOnlyForTheDelegateTypeItNames(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("DynamicInvokePerType", """
            public delegate int Twice(int value);
            public delegate long Widen(int value);
            public delegate string Describe(int value);

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Widen widen = value => value;
                    Describe describe = value => value.ToString();
                    Twice twice = value => value * 2;
                    var direct = (int)widen(input) + describe(input).Length;
                    return (int)twice.DynamicInvoke(input)! + direct - 2 * input - 1;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", [], target));

        var invocation = Assert.IsType<DelegateDynamicInvokePlan>(result.Program.MemberExecution.DelegateInvocation);
        Assert.Equal("Twice", Assert.Single(invocation.Methods.Values).DeclaringType.FullName);
        Assert.DoesNotContain(result.Program.MethodDescriptors.Values, method =>
            method.Definition.Name == "Invoke" && method.DeclaringType.FullName is "Widen" or "Describe");
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    // Through Delegate the delegate's type is known only at run time: a parameterless Action, or a
    // parameterless Func of a reference (a Func<object?> by variance), runs as on .NET, with its
    // argument count checked and its exceptions wrapped; any other delegate has no Invoke metadata.
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void DynamicInvokeThroughDelegateRunsOnlyWhatOneSignatureCovers(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("DynamicInvokeUnknownType", """
            using System;
            using System.Reflection;

            public delegate object Factory();

            public static class EntryPoint
            {
                private static int _calls;

                private static int Outcome(Delegate callback, object?[]? arguments, Func<object?, bool> expected)
                {
                    try
                    {
                        return expected(callback.DynamicInvoke(arguments)) ? 1 : 0;
                    }
                    catch (TargetInvocationException exception)
                    {
                        return exception.InnerException is InvalidOperationException ? 2 : 0;
                    }
                    catch (TargetParameterCountException)
                    {
                        return 3;
                    }
                    catch (PlatformNotSupportedException)
                    {
                        return 4;
                    }
                }

                public static int Run(int input)
                {
                    Action count = () => _calls++;
                    var outcomes = new[]
                    {
                        Outcome(count, null, result => result is null && _calls == 1),
                        Outcome(new Func<string>(() => "value"), [], result => (string?)result == "value"),
                        Outcome(count + count, null, result => result is null && _calls == 3),
                        Outcome(new Func<string>(() => throw new InvalidOperationException()), null, _ => false),
                        Outcome(count, [input], _ => false),
                        Outcome(new Func<int>(() => input), null, _ => false),
                        Outcome(new Factory(() => "value"), null, _ => false),
                        Outcome(new Func<int, int>(value => value), [input], _ => false),
                    };
                    var expected = new[] { 1, 1, 1, 2, 3, 4, 4, 4 };
                    var passed = 0;
                    for (var index = 0; index < outcomes.Length; index++)
                    {
                        if (outcomes[index] == expected[index]) passed |= 1 << index;
                    }
                    return passed;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", [], target));

        Assert.Equal(255, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
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
