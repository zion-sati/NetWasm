// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Adler-32 as a PNG encoder writes it: the running sums arrive by ref, and the loop updates them for every byte.
// Inlined at the speed level where the caller passes its own locals, the loop updates those locals, which stay in
// registers, also across the span indexer's bounds check, whose branch around its throw splits the update.
public sealed class AddressLoopInliningCompilationTests
{
    private const string Source = """
        using System;

        public static class Adler
        {
            internal static void Update(ref uint a, ref uint b, ReadOnlySpan<byte> data)
            {
                int count;
                for (int offset = 0; offset < data.Length; offset += count)
                {
                    count = Math.Min(5552, data.Length - offset);
                    for (int index = 0; index < count; index++)
                    {
                        a += data[offset + index];
                        b += a;
                    }
                    a %= 65521;
                    b %= 65521;
                }
            }
        }

        public static class EntryPoint
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public static int Checksum(byte[] data)
            {
                uint a = 1, b = 0;
                Adler.Update(ref a, ref b, data);
                return (int)(b << 16 | a);
            }

            public static int Run(int input)
            {
                var data = new byte[12000 + input];
                for (var index = 0; index < data.Length; index++) data[index] = (byte)(index * 7 + input);
                // A lambda over a captured array, as a benchmark measures it.
                Func<int> measured = () => { uint a = 1, b = 0; Adler.Update(ref a, ref b, data); return (int)(b << 16 | a); };
                return Checksum(data) ^ measured() ^ Checksum(data);
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    public void SpeedBuildsKeepALoopsByRefSumsInTheCallersRegisters(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("AddressLoop", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        // The sums no longer have an address, so the caller keeps no frame in memory for them.
        foreach (var caller in new[] { "EntryPoint::Checksum", "EntryPoint+<>c__DisplayClass1_0::<Run>b__0" })
        {
            Assert.Equal(preset != FinalWasmOptimization.O3,
                Function(assets, result, caller).Contains("value_frame_enter", StringComparison.Ordinal));
        }

        // As desktop .NET computes them.
        foreach (var (input, expected) in new[] { (0, 103045194), (3, 1170561128) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    private static string Function(TestAssets assets, CompilationResult result, string name)
    {
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var function = Regex.Match(printed.StandardOutput,
            $@"\(func \${Regex.Escape(name)}\b.*?(?=\n  \(func |\n  \(type |\z)", RegexOptions.Singleline);
        Assert.True(function.Success, $"{name} was not emitted.");
        return function.Value;
    }
}
