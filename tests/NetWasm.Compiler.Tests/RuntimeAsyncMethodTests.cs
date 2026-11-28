// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// An assembly built with runtime-async=on marks its async methods MethodImplOptions.Async (0x2000). Until the compiler
// lowers runtime async, a reached one names itself; one the program never reaches costs nothing.
public sealed class RuntimeAsyncMethodTests
{
    private const string Source = """
        public static class EntryPoint
        {
            public static int Reached(int value) => value + 1;

            public static int Unreached(int value) => value - 1;

            public static int Run(int value) => Reached(value);
        }
        """;

    [Fact]
    public void AReachedRuntimeAsyncMethodNamesItself()
    {
        using var assets = TestAssets.Create();
        var assembly = MarkRuntimeAsync(assets.CompileSource("RuntimeAsyncFixture", Source), "Reached");

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(
            new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [])));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
        Assert.Contains("EntryPoint::Reached", exception.Diagnostic.Method, StringComparison.Ordinal);
        Assert.Contains("runtime-async", exception.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreachedRuntimeAsyncMethodCostsNothing()
    {
        using var assets = TestAssets.Create();
        var plain = assets.CompileSource("RuntimeAsyncFixture", Source);
        var expected = NetWasmCompiler.Compile(new CompilerOptions(plain, [assets.CoreLib], "EntryPoint", "Run", []))
            .ApplicationModule;
        var assembly = MarkRuntimeAsync(plain, "Unreached");

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", []));

        Assert.Equal(expected, result.ApplicationModule);
    }

    // Roslyn's runtime-async=on, against a CoreLib that defines AsyncHelpers, compiles an async method without a state
    // machine and marks it MethodImplOptions.Async; NetWasm lowers it. Here every await completes at once, in a chain
    // of async calls and in a loop, whose header the resume dispatch goes through.
    [Theory]
    [InlineData(FinalWasmOptimization.None, false)]
    [InlineData(FinalWasmOptimization.Oz, false)]
    [InlineData(FinalWasmOptimization.Oz, true)]
    public void RoslynsRuntimeAsyncOutputRunsLowered(FinalWasmOptimization optimization, bool optimizedIl)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "RuntimeAsyncRoslynFixture",
            """
            using System.Threading.Tasks;

            public static class EntryPoint
            {
                private static async Task<int> Add(int value)
                {
                    var known = await Task.FromResult(value);
                    return known + 1;
                }

                private static async ValueTask<int> Sum(int count)
                {
                    var total = 0;
                    for (var index = 0; index < count; index++)
                        total += await Add(index);
                    return total;
                }

                private static int Side() => 3;

                private static int s_guarded;

                // In optimized IL the body starts with the protected block, which the resumption enters by falling in.
                private static async Task<int> Guarded(int value)
                {
                    try
                    {
                        return await Add(value) - 1;
                    }
                    finally
                    {
                        s_guarded++;
                    }
                }

                // In optimized IL this body starts with a protected block that holds no await, as
                // SemaphoreSlim.WaitUntilCountOrTimeoutAsync's does: the dispatch enters it by falling in as well.
                private static async Task<int> Prepared(int value)
                {
                    int known;
                    try
                    {
                        known = Side() + value;
                    }
                    catch
                    {
                        s_guarded += 100;
                        throw;
                    }
                    return await Add(known);
                }

                // A loop whose body opens with a protected block: Roslyn's optimized IL puts a nop before the block, so
                // the loop's header and the block's start are separate points the dispatch passes on the way to the
                // await inside the block, and only the header on the way to the one after it.
                private static async Task<int> Looped(int count)
                {
                    var total = 0;
                    var index = 0;
                    while (index < count)
                    {
                        try
                        {
                            total += await Add(index);
                        }
                        catch (System.InvalidOperationException)
                        {
                            total -= 100;
                        }
                        index++;
                        total += await Add(0);
                    }
                    return total;
                }

                // The same with no await in the protected block.
                private static async Task<int> LoopedQuietly(int count)
                {
                    var total = 0;
                    var index = 0;
                    while (index < count)
                    {
                        try
                        {
                            total += Side();
                        }
                        catch (System.InvalidOperationException)
                        {
                            total -= 100;
                        }
                        index++;
                        total += await Add(0);
                    }
                    return total;
                }

                private static int Combine(int left, int right) => left - right;

                private static async Task<int> Twice(int value)
                {
                    var once = await Add(value);
                    await Task.CompletedTask;
                    return await Add(once) + await Sum(3) - 6 + Combine(Side(), await Add(2));
                }

                public static int Run(int value) =>
                    Twice(value).Result + Guarded(1).Result - 1 + s_guarded - 1 + Prepared(1).Result - 5 +
                    Looped(3).Result - 9 + LoopedQuietly(3).Result - 12;
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimizedIl,
            features: ["runtime-async=on"]);
        using (var portableExecutable = new PEReader(File.OpenRead(assembly)))
        {
            var metadata = portableExecutable.GetMetadataReader();
            var add = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition)
                .Single(method => metadata.GetString(method.Name) == "Add");
            Assert.Equal(0x2000, (int)add.ImplAttributes & 0x2000);
            Assert.DoesNotContain(metadata.TypeDefinitions, handle =>
                metadata.GetString(metadata.GetTypeDefinition(handle).Name).Contains("d__", StringComparison.Ordinal));
        }

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: optimization));

        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 40, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // A method with more parameters than a frame's resumer delegate takes (Func<T1, ..., T16, TResult>) was named
    // (NW1001); it now lowers, its frame resuming through a stub the compiler provides beside it (RuntimeAsyncResumers).
    // RuntimeAsyncExecutionTests suspends such methods; here every await completes at once.
    [Fact]
    public void AMethodWithMoreParametersThanAResumerTakesLowers()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "RuntimeAsyncShapes",
            """
            using System;
            using System.Threading.Tasks;

            public static class EntryPoint
            {
                private static async Task<int> Step(int value) => await Task.FromResult(value);

                private static async Task<int> Many(int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9,
                    int a10, int a11, int a12, int a13, int a14, int a15, int a16, int a17) =>
                    await Step(a1 + a17) + a9;

                public static int Run(int value) =>
                    Many(value, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17).Result;
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            features: ["runtime-async=on"]);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", []));

        Assert.Equal(66, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 40, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // Sets MethodImplOptions.Async on the named method in the assembly's MethodDef table, as runtime-async=on does.
    private static string MarkRuntimeAsync(string assemblyPath, string methodName)
    {
        var bytes = File.ReadAllBytes(assemblyPath);
        int offset;
        using (var portableExecutable = new PEReader(new MemoryStream(bytes)))
        {
            var metadata = portableExecutable.GetMetadataReader();
            var handle = metadata.MethodDefinitions.Single(candidate =>
                metadata.GetString(metadata.GetMethodDefinition(candidate).Name) == methodName);
            offset = portableExecutable.PEHeaders.MetadataStartOffset +
                metadata.GetTableMetadataOffset(TableIndex.MethodDef) +
                (MetadataTokens.GetRowNumber(handle) - 1) * metadata.GetTableRowSize(TableIndex.MethodDef) +
                sizeof(int);
        }
        var flags = (ushort)(bytes[offset] | bytes[offset + 1] << 8) | 0x2000;
        bytes[offset] = (byte)flags;
        bytes[offset + 1] = (byte)(flags >> 8);
        var marked = Path.ChangeExtension(assemblyPath, ".async.dll");
        File.WriteAllBytes(marked, bytes);
        return marked;
    }
}
