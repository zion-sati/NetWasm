// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.ModuleEncoding;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A method whose function comes out over an engine limit only because of what O3 inlines into it compiles again
// with nothing inlined into it, and runs as before; a method over the limit even then still fails the build with
// the limit's diagnostic. The limits are lowered here so a small program reaches them; a program that fits is
// compiled once, exactly as before.
public sealed class FunctionLimitRecompilationTests
{
    private const int Calls = 64;

    private static readonly string Source = $$"""
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int Mix(int x)
            {
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                x = x * 1103515245 + 12345;
                x ^= x >> 11;
                x ^= x << 7;
                return x * 31 + 7;
            }

            public static int Run(int input)
            {
                var x = input;
                {{string.Concat(Enumerable.Repeat("x = Mix(x);\n", Calls))}}
                return x;
            }
        }
        """;

    [Fact]
    public void AMethodOverTheLimitOnlyThroughInliningCompilesWithoutItAndRuns()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("FunctionLimitRecompilation", Source);

        var inlined = Compile(assets, assembly, new WasmFunctionLimits());
        var sizes = FunctionSizes(inlined.ApplicationModule);
        var run = sizes["EntryPoint::Run"];
        Assert.DoesNotContain("EntryPoint::Mix", sizes.Keys);
        Assert.True(sizes.Where(static pair => pair.Key != "EntryPoint::Run").Max(static pair => pair.Value) < run - 1,
            "Run, with Mix inlined, must be the program's largest function for this test.");

        var recompiled = Compile(assets, assembly, new WasmFunctionLimits(MaximumBodySize: run - 1));
        var recompiledSizes = FunctionSizes(recompiled.ApplicationModule);

        Assert.True(recompiledSizes["EntryPoint::Run"] < run - 1);
        Assert.Contains("EntryPoint::Mix", recompiledSizes.Keys);
        foreach (var input in new[] { 1, 42, -7 })
        {
            Assert.Equal(Expected(input), ExecuteWithStandardWasiNode(
                recompiled.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, recompiled.StaticDataEnd));
            Assert.Equal(Expected(input), ExecuteWithStandardWasiNode(
                inlined.ApplicationModule, assets.Directory, input, WasmTarget.Wasm32, inlined.StaticDataEnd));
        }
    }

    [Fact]
    public void AMethodOverTheLimitWithoutInliningStillFailsTheBuild()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("FunctionLimitRecompilationTooLarge", Source);
        var inlined = FunctionSizes(Compile(assets, assembly, new WasmFunctionLimits()).ApplicationModule);
        var others = inlined.Where(static pair => pair.Key != "EntryPoint::Run").Max(static pair => pair.Value);
        var withoutInlining = FunctionSizes(Compile(assets, assembly,
            new WasmFunctionLimits(MaximumBodySize: inlined["EntryPoint::Run"] - 1)).ApplicationModule)["EntryPoint::Run"];
        Assert.True(others < withoutInlining - 1, "Run, without Mix inlined, must still be over the limit below.");

        var error = Assert.Throws<CompilerException>(() => Compile(assets, assembly,
            new WasmFunctionLimits(MaximumBodySize: withoutInlining - 1)));

        Assert.Equal("NW1013", error.Diagnostic.Id);
        Assert.Equal("EntryPoint::Run", error.Diagnostic.Method);
        Assert.EndsWith("limit that browsers and Node enforce; split it into smaller methods", error.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    private static CompilationResult Compile(TestAssets assets, string assembly, WasmFunctionLimits limits)
    {
        using var services = new ServiceCollection().AddSingleton(limits).AddNetWasmCompiler().BuildServiceProvider();
        return services.GetRequiredService<INetWasmCompiler>().Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: WasmTarget.Wasm32, Optimization: FinalWasmOptimization.O3));
    }

    private static int Expected(int input)
    {
        var x = input;
        for (var call = 0; call < Calls; call++)
        {
            unchecked
            {
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                x = x * 1103515245 + 12345;
                x ^= x >> 11;
                x ^= x << 7;
                x = x * 31 + 7;
            }
        }
        return x;
    }

    // Each defined function's body size, by the name the module's name section gives it.
    private static Dictionary<string, int> FunctionSizes(byte[] module)
    {
        var imports = 0;
        (int Start, int End)? code = null;
        var names = new Dictionary<int, string>();
        for (var position = 8; position < module.Length;)
        {
            var id = module[position];
            var size = (int)Unsigned(module, ref position, 1);
            var end = position + size;
            if (id == 2)
            {
                var at = position;
                for (var count = Unsigned(module, ref at); count > 0; count--)
                {
                    // The module's and the field's names.
                    for (var name = 0; name < 2; name++)
                    {
                        var nameLength = (int)Unsigned(module, ref at);
                        at += nameLength;
                    }
                    var kind = module[at++];
                    switch (kind)
                    {
                        case 0:
                            imports++;
                            Unsigned(module, ref at);
                            break;
                        case 1:
                            at++;
                            if ((Unsigned(module, ref at) & 1) != 0) Unsigned(module, ref at);
                            Unsigned(module, ref at);
                            break;
                        case 2:
                            var flags = Unsigned(module, ref at);
                            Unsigned(module, ref at);
                            if ((flags & 1) != 0) Unsigned(module, ref at);
                            break;
                        case 3:
                            at += 2;
                            break;
                        default:
                            at++;
                            Unsigned(module, ref at);
                            break;
                    }
                }
            }
            else if (id == 10)
            {
                code = (position, end);
            }
            else if (id == 0)
            {
                var at = position;
                var length = (int)Unsigned(module, ref at);
                if (module.AsSpan(at, length).SequenceEqual("name"u8))
                {
                    for (at += length; at < end;)
                    {
                        var subsection = module[at];
                        var subsectionSize = (int)Unsigned(module, ref at, 1);
                        if (subsection == 1)
                        {
                            var entry = at;
                            for (var count = Unsigned(module, ref entry); count > 0; count--)
                            {
                                var index = (int)Unsigned(module, ref entry);
                                var nameLength = (int)Unsigned(module, ref entry);
                                names[index] = System.Text.Encoding.UTF8.GetString(module, entry, nameLength);
                                entry += nameLength;
                            }
                        }
                        at += subsectionSize;
                    }
                }
            }
            position = end;
        }
        Assert.True(code is not null, "The module has no code section.");
        var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
        var body = code.Value.Start;
        var functions = (int)Unsigned(module, ref body);
        for (var function = 0; function < functions; function++)
        {
            var size = (int)Unsigned(module, ref body);
            if (names.TryGetValue(imports + function, out var name)) sizes[name] = size;
            body += size;
        }
        return sizes;
    }

    private static ulong Unsigned(byte[] bytes, ref int position, int skip = 0)
    {
        position += skip;
        ulong value = 0;
        for (var shift = 0; ; shift += 7)
        {
            var next = bytes[position++];
            value |= (ulong)(next & 0x7f) << shift;
            if ((next & 0x80) == 0) return value;
        }
    }
}
