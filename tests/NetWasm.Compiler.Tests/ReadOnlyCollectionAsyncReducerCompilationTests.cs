using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ReadOnlyCollectionAsyncReducerCompilationTests
{
    [Fact]
    public void DesktopOracleMatchesArrayAndListBackedAsyncForeachAcrossProfilesAndTargets()
    {
        var expected = RunDesktopOracle(41);
        Assert.Equal(3066063, expected);

        foreach (var optimize in new[] { false, true })
        {
            foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
            {
                using var assets = TestAssets.Create();
                ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

                var observed = executor.Execute(new CompilationScenario(
                    "ReadOnlyCollectionAsyncReducerFixture",
                    Source,
                    "ReadOnlyCollectionAsyncReducerFixture.EntryPoint",
                    optimize,
                    target,
                    41,
                    [])
                {
                    DrainReactor = true,
                    ObserveExportName = "observe",
                    WitPath = Path.Combine(
                        assets.Root,
                        "wit",
                        "netwasm-platform-1.0.0"),
                    WitWorld = "netwasm:platform/async-platform@1.0.0",
                    Exports =
                    [
                        new RequestedExport(
                            "run",
                            "ReadOnlyCollectionAsyncReducerFixture.EntryPoint",
                            "Run"),
                        new RequestedExport(
                            "observe",
                            "ReadOnlyCollectionAsyncReducerFixture.EntryPoint",
                            "Observe"),
                    ],
                });

                Assert.Equal(expected, observed);
            }
        }
    }

    private static int RunDesktopOracle(int input)
    {
        var compilation = CSharpCompilation.Create(
            "ReadOnlyCollectionAsyncReducer.Desktop",
            [CSharpSyntaxTree.ParseText(
                Source,
                new CSharpParseOptions(LanguageVersion.Latest))],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assemblyStream = new MemoryStream();
        var emit = compilation.Emit(assemblyStream);
        Assert.True(emit.Success);

        assemblyStream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream);
        var run = assembly.GetType(
                "ReadOnlyCollectionAsyncReducerFixture.EntryPoint")!
            .GetMethod("RunSync", BindingFlags.Public | BindingFlags.Static)!;
        return (int)run.Invoke(null, [input])!;
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
        .Split(Path.PathSeparator)
        .Select(path => MetadataReference.CreateFromFile(path));

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
            using System.Threading.Tasks;

        namespace ReadOnlyCollectionAsyncReducerFixture;

        public sealed class WorkItem
        {
            public WorkItem(int index)
            {
                Value = index + 3;
                Fallback = 100 + index;
                Fails = index % 5 == 1;
                Payload = new WorkPayload(index);
            }

            public int Value { get; }
            public int Fallback { get; }
            public bool Fails { get; }
            public WorkPayload Payload { get; }
        }

        public sealed class WorkPayload
        {
            public WorkPayload(int index)
            {
                Code = index * 3 + 1;
                Bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            }

            public int Code { get; }
            public byte[] Bytes { get; }
        }

        public static class EntryPoint
        {
            private static Task<int>? _pending;
            private const int WorkItemCount = 33;

            public static int Run(int input)
            {
                _pending = ExecuteAsync(input);
                return 0;
            }

            public static int Observe()
            {
                if (_pending is null || !_pending.IsCompleted)
                {
                    return -1;
                }

                try
                {
                    return _pending.GetAwaiter().GetResult();
                }
                catch
                {
                    return -2;
                }
            }

            public static int RunSync(int input) =>
                ExecuteAsync(input).GetAwaiter().GetResult();

            private static async Task<int> ExecuteAsync(int input)
            {
                var arrayResult = await AccumulateAsync(ArrayBacked(), input);
                var listResult = await AccumulateAsync(ListBacked(), input);
                return arrayResult * 1000 + listResult;
            }

            private static async Task<int> AccumulateAsync(
                IReadOnlyList<WorkItem> items,
                int input)
            {
                var total = input;
                var index = 0;
                var apply = new Func<WorkItem, int, ValueTask<int>>(ApplyAsync);
                var finish = new Func<int, int, ValueTask<int>>(FinishAsync);
                foreach (var item in items)
                {
                    var scratch = new byte[8];
                    scratch[0] = (byte)(index + 1);
                    GC.Collect();
                    try
                    {
                        var applied = await apply(item, index);
                        var finished = await finish(applied + scratch[0], index);
                        total += finished - scratch[0];
                    }
                    catch (InvalidOperationException)
                    {
                        total += item.Fallback;
                    }

                    index++;
                }

                return total;
            }

            private static IReadOnlyList<WorkItem> ArrayBacked()
            {
                var items = new WorkItem[WorkItemCount];
                for (var index = 0; index < items.Length; index++)
                {
                    items[index] = new WorkItem(index);
                }

                return new ReadOnlyCollection<WorkItem>(items);
            }

            private static IReadOnlyList<WorkItem> ListBacked()
            {
                var source = new List<WorkItem>(WorkItemCount);
                for (var index = 0; index < WorkItemCount; index++)
                {
                    source.Add(new WorkItem(index));
                }

                return new ReadOnlyCollection<WorkItem>(source);
            }

            private static ValueTask<int> ApplyAsync(WorkItem item, int index)
            {
                if ((index & 1) == 0)
                {
                    return item.Fails
                        ? new ValueTask<int>(Task.FromException<int>(
                            new InvalidOperationException()))
                        : new ValueTask<int>(
                            item.Value + item.Payload.Code + item.Payload.Bytes[0] + index);
                }

                return new ValueTask<int>(ApplySuspendedAsync(item, index));
            }

            private static async Task<int> ApplySuspendedAsync(
                WorkItem item,
                int index)
            {
                await Task.Yield();
                if (item.Fails)
                {
                    throw new InvalidOperationException();
                }

                return item.Value + item.Payload.Code + item.Payload.Bytes[0] + index;
            }

            private static ValueTask<int> FinishAsync(int value, int index) =>
                (index & 1) == 0
                    ? new ValueTask<int>(value)
                    : new ValueTask<int>(FinishSuspendedAsync(value));

            private static async Task<int> FinishSuspendedAsync(int value)
            {
                await Task.Yield();
                return value;
            }
        }
        """;
}
