// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Wasmtime saves every callee-saved register on each call to a function that contains a
// throw instruction. Throws therefore go through the module's shared throw functions, and
// only those and functions with their own handlers may hold a throw instruction.
public sealed partial class ManagedThrowSiteCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.ExceptionServices;

        public sealed class Holder
        {
            public int Value;
        }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Read(Holder holder) => holder.Value;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Add(int left, int right) => checked(left + right);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Element(int[] values, int index) => values[index];

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Positive(int value) =>
                value < 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : value;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Rethrow(Exception captured) => ExceptionDispatchInfo.Capture(captured).Throw();

            public static int Run(int input)
            {
                var holder = input == 1 ? null : new Holder { Value = 2 };
                var values = new[] { 3, 4 };
                var total = 0;
                try { total += Read(holder!); } catch (NullReferenceException) { total += 10; }
                try { total += Add(input == 2 ? int.MaxValue : 5, 1); } catch (OverflowException) { total += 100; }
                try { total += Element(values, input == 3 ? 2 : 1); } catch (IndexOutOfRangeException) { total += 1000; }
                try { total += Positive(input == 4 ? -1 : 7); } catch (ArgumentOutOfRangeException) { total += 10000; }
                var original = new InvalidOperationException("kept");
                try { Rethrow(original); } catch (InvalidOperationException caught) when (ReferenceEquals(caught, original)) { total += 100000; }
                return total;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void MethodsThrowThroughTheSharedThrowFunction(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ManagedThrowSites", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);

        var functions = FunctionHeader().Split(printed.StandardOutput).Skip(1)
            .Select(static function => (Name: function[..function.IndexOf(' ')], Body: function))
            .ToArray();
        var throwing = functions
            .Where(static function => ThrowInstruction().IsMatch(function.Body) &&
                !function.Body.Contains("try_table", StringComparison.Ordinal))
            .Select(static function => function.Name)
            .ToArray();
        Assert.Equal(["$managed.throw", "$managed.rethrow"], throwing);
        foreach (var name in new[] { "Read", "Add", "Element", "Positive" })
        {
            var body = Assert.Single(functions, function => function.Name == $"$EntryPoint::{name}").Body;
            Assert.Contains("call $managed.throw", body, StringComparison.Ordinal);
        }

        foreach (var (input, expected) in new[] { (0, 100019), (1, 100027), (2, 100113), (3, 101015), (4, 110012) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"\n\s*\(func ")]
    private static partial Regex FunctionHeader();

    [GeneratedRegex(@"^\s*throw\s", RegexOptions.Multiline)]
    private static partial Regex ThrowInstruction();
}
