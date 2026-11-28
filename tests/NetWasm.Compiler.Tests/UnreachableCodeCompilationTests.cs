// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// Code no path from a method's entry or one of its handlers reaches never runs, so nothing only it names is reached,
// rooted or compiled. Each fixture compiles under the receiver flow audit, which fails a build whose closure compiled a
// method the flow did not reach: the flow skips such code, so the closure must too.
public sealed class UnreachableCodeCompilationTests
{
    // Load never returns once Fail is known to only throw, so inlining it leaves the code after the call unreachable:
    // a call, an allocation, a static field, a delegate, a type test, a string and a catch clause only that code names.
    private const string AfterAnInlinedFailure = """
        using System;
        using System.Runtime.CompilerServices;

        public sealed class OnlyAfterFailure
        {
            public OnlyAfterFailure(int value) => Value = value;
            public int Value { get; }
        }

        public sealed class TestedAfterFailure { }

        public sealed class CaughtAfterFailureException : Exception { }

        public static class ReadAfterFailure
        {
            public static int Value = Seed();

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Seed() => 3;
        }

        public static class Helper
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int Load(int value)
            {
                Fail();
                return value + 1;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void Fail() => throw new InvalidOperationException();

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Wide(int value) => value * 1000 + 7;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Bound(int value) => value - 1;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static object Boxed(int value) => value;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (input == 99)
                {
                    var loaded = Helper.Load(input);
                    var total = Helper.Wide(loaded) + new OnlyAfterFailure(loaded).Value + ReadAfterFailure.Value;
                    Func<int, int> bound = Helper.Bound;
                    total += bound(loaded);
                    if (Helper.Boxed(total) is TestedAfterFailure) total++;
                    try
                    {
                        total += "only after the failure".Length;
                    }
                    catch (CaughtAfterFailureException)
                    {
                        total--;
                    }
                    return total;
                }
                return input + 5;
            }
        }
        """;

    // Base64 decoding: its AVX2 loop, inlined into DecodeFrom, calls a vector load that never returns where AVX2 is
    // unsupported, so the decoder's TryDecode256Core that follows it is unreachable.
    private const string Base64 = """
        using System;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var bytes = Convert.FromBase64String(input == 7 ? "AQIDBAUG" : "AQI=");
                return bytes.Length + bytes[^1];
            }
        }
        """;

    private static readonly string[] OnlyAfterTheFailure =
        ["OnlyAfterFailure", "TestedAfterFailure", "CaughtAfterFailureException", "ReadAfterFailure"];

    [Theory]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void NothingOnlyUnreachableCodeNamesIsCompiled(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var result = Compile(assets.CompileSource("UnreachableAfterFailureFixture", AfterAnInlinedFailure),
            assets, optimization);

        var methods = Methods(result);
        Assert.DoesNotContain(methods, method => method.Definition.Name is "Wide" or "Bound" or "Boxed");
        Assert.DoesNotContain(methods, method => OnlyAfterTheFailure.Contains(method.DeclaringType.FullName));
        var types = result.Program.AllocatedTypes.Concat(result.Program.ConstructedTypes)
            .Concat(result.Program.RuntimeTypes)
            .Select(type => type.FullName)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(types, OnlyAfterTheFailure.Contains);
        Assert.DoesNotContain("only after the failure", result.Program.StringLiterals);
        Assert.Equal(12, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 7, WasmTarget.Wasm32,
            result.StaticDataEnd));
    }

    [Theory]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void Base64sUnreachableVectorLoopIsNotCompiled(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var result = Compile(assets.CompileOptimizedSource("UnreachableBase64Fixture", Base64), assets, optimization);

        Assert.DoesNotContain(Methods(result), method => method.Definition.Name == "TryDecode256Core");
        Assert.Equal(12, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 7, WasmTarget.Wasm32,
            result.StaticDataEnd));
    }

    private static CompilationResult Compile(string assembly, TestAssets assets, FinalWasmOptimization optimization) =>
        NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

    private static MethodInstanceModel[] Methods(CompilationResult result) =>
        [.. result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values).Select(body => body.Method)];
}
