// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Enum.IsDefined<TEnum> leaves its answer in the call's result local, as every intrinsic does. Its emitter also pushed
// the answer onto the Wasm stack, which only validated while the call stayed in its own function under a return. Once
// inlined into ordinary flow (SharpCompress's ZipFileEntry.LoadExtra tests IsDefined in an if), a block ended with that
// value still on the stack and wasm-opt rejected the module. The fixture tests each underlying width, both overloads and
// both outcomes from branch conditions inside a loop.
public sealed class EnumIsDefinedFlowCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O2)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O2)]
    public void IsDefinedInBranchConditionsValidatesAndRuns(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("EnumIsDefinedFlowFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EnumIsDefinedFlowFixture.EntryPoint", "Run",
            [], Target: target, Optimization: optimization));
        var path = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(path, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var validated = services.GetRequiredService<IWasmTools>().Run("validate", "--features", "all", path);
        Assert.True(validated.ExitCode == 0, validated.StandardError);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Fact]
    public void FixtureMatchesDesktop()
    {
        var compilation = CSharpCompilation.Create("EnumIsDefinedFlow.Desktop",
            [CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.Preview),
                cancellationToken: TestContext.Current.CancellationToken)],
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!.Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emit.Success, string.Join(Environment.NewLine,
            emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        stream.Position = 0;
        var run = new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(stream)
            .GetType("EnumIsDefinedFlowFixture.EntryPoint")!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
        Assert.Equal(42, run.Invoke(null, [41]));
    }

    private const string Source = """
        using System;
        using System.Buffers.Binary;
        using System.Collections.Generic;

        namespace EnumIsDefinedFlowFixture;

        public enum ExtraDataType : ushort { Zip64 = 0x0001, UnixTime = 0x5455, UnicodePath = 0x7075, Aes = 0x9901, NotImplemented = 0xFFFF }
        public enum Small : byte { A = 1, B = 200 }
        public enum Wide : long { Low = -5, High = 1L << 40 }
        public enum Huge : ulong { Top = ulong.MaxValue }
        public enum Plain { Zero, Two = 2 }

        public sealed class Entry
        {
            public List<(ExtraDataType Type, int Length)> Extras { get; } = new();

            private static bool IsDefined(ExtraDataType type) => Enum.IsDefined(type);

            // SharpCompress's ZipFileEntry.LoadExtra.
            public void LoadExtra(byte[] extra)
            {
                for (var i = 0; i < extra.Length; )
                {
                    if (i + 4 > extra.Length) break;
                    var type = (ExtraDataType)BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(i));
                    if (!IsDefined(type)) type = ExtraDataType.NotImplemented;
                    var length = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(i + 2));
                    if (length > extra.Length) break;
                    if (i + 4 + length > extra.Length) break;
                    Extras.Add((type, length));
                    i += length + 4;
                }
            }
        }

        public static class EntryPoint
        {
            private static int Count<T>(T[] values) where T : struct, Enum
            {
                var count = 0;
                foreach (var value in values)
                {
                    if (Enum.IsDefined(value)) count++;
                    else count += 10;
                }
                return count;
            }

            public static int Run(int input)
            {
                var entry = new Entry();
                entry.LoadExtra([0x01, 0x00, 0x02, 0x00, 7, 9, 0x41, 0x43, 0x01, 0x00, 5, 0x75, 0x70, 0x00, 0x00, 0x55]);
                if (entry.Extras.Count != 3) return 1;
                if (entry.Extras[0] != (ExtraDataType.Zip64, 2)) return 2;
                if (entry.Extras[1] != (ExtraDataType.NotImplemented, 1)) return 3;
                if (entry.Extras[2] != (ExtraDataType.UnicodePath, 0)) return 4;

                if (Count(new[] { Small.A, (Small)2, Small.B }) != 12) return 5;
                if (Count(new[] { Wide.Low, (Wide)(1L << 39), Wide.High }) != 12) return 6;
                if (Count(new[] { Huge.Top, (Huge)0 }) != 11) return 7;
                if (Count(new[] { Plain.Zero, (Plain)1, Plain.Two }) != 12) return 8;

                var hits = 0;
                for (var raw = 0; raw < 4; raw++)
                {
                    if (Enum.IsDefined(typeof(Plain), raw)) hits++;
                    if (Enum.IsDefined((Plain)raw)) hits += 100;
                }
                if (hits != 202) return 9;
                return input + 1;
            }
        }
        """;
}
