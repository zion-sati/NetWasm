using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ControlFlowOwnershipCompilationTests
{
    [Fact]
    public void NestedLoopContinuationsInsideEnumerationCleanupPassDesktopOracle()
    {
        var compilation = CSharpCompilation.Create(
            "ControlFlowOwnership.Desktop",
            [CSharpSyntaxTree.ParseText(
                Source,
                new CSharpParseOptions(LanguageVersion.Preview))],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assemblyStream = new MemoryStream();
        var emit = compilation.Emit(assemblyStream);
        Assert.True(
            emit.Success,
            string.Join(Environment.NewLine, emit.Diagnostics.Where(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)));

        assemblyStream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream);
        var run = assembly.GetType("ControlFlowOwnership.Application.EntryPoint")!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

        Assert.Equal(6_161, run.Invoke(null, [4]));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesNestedLoopContinuationsInsideEnumerationCleanup(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "ControlFlowOwnership.Application",
            Source,
            "ControlFlowOwnership.Application.EntryPoint",
            optimize,
            target,
            Input: 4,
            ReferencePaths: ImmutableArray<string>.Empty));

        Assert.Equal(6_161, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesAsyncCleanupDispatchersWithoutRepeatingLeaves(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "AsyncControlFlowOwnership.Application",
            AsyncSource,
            "AsyncControlFlowOwnership.Application.EntryPoint",
            optimize,
            target,
            Input: 4,
            ReferencePaths: ImmutableArray<string>.Empty)
        {
            Exports =
            [
                new RequestedExport("run", "AsyncControlFlowOwnership.Application.EntryPoint", "Run"),
                new RequestedExport("observe", "AsyncControlFlowOwnership.Application.EntryPoint", "Observe"),
            ],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/async-platform@1.0.0",
            DrainReactor = true,
            ObserveExportName = "observe",
        });

        Assert.Equal(1_005, result);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Generic;

        namespace ControlFlowOwnership.Application;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var enumeration = RunEnumeration(input);
                var resource = RunResource(input);
                return enumeration * 100 + resource;
            }

            private static int RunEnumeration(int input)
            {
                Values.DisposeCount = 0;
                var result = 0;
                foreach (var item in new Values(input))
                {
                    for (var index = 0; index < input; index++)
                    {
                        if (index < item)
                        {
                            continue;
                        }

                        if (index == item)
                        {
                            result += index;
                            break;
                        }

                        return -1;
                    }
                }

                return result * 10 + Values.DisposeCount;
            }

            private static int RunResource(int input)
            {
                Resource.DisposeCount = 0;
                var result = 0;
                var item = 0;
                using (new Resource())
                {
                    do
                    {
                        for (var index = 0; index < input; index++)
                        {
                            if (index < item)
                            {
                                continue;
                            }

                            if (index == item)
                            {
                                result += index;
                                break;
                            }

                            return -1;
                        }

                        item++;
                    }
                    while (item < input);
                }

                return result * 10 + Resource.DisposeCount;
            }

        }

        public sealed class Resource : IDisposable
        {
            public static int DisposeCount;

            public void Dispose() => DisposeCount++;
        }

        public sealed class Values(int count) : IEnumerable<int>
        {
            public static int DisposeCount;

            public IEnumerator<int> GetEnumerator() => new Enumerator(count);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator(int count) : IEnumerator<int>
            {
                private int _current = -1;

                public int Current => _current;

                object IEnumerator.Current => Current;

                public bool MoveNext() => ++_current < count;

                public void Reset() => _current = -1;

                public void Dispose()
                {
                    DisposeCount++;
                }
            }
        }
        """;

    private const string AsyncSource = """
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        namespace AsyncControlFlowOwnership.Application;

        public static class EntryPoint
        {
            private static Task<int>? _pending;

            public static int Run(int input)
            {
                _pending = RunAsync(input);
                return _pending.IsCompleted ? _pending.GetAwaiter().GetResult() : -1;
            }

            public static int Observe() =>
                _pending is { IsCompleted: true }
                    ? _pending.GetAwaiter().GetResult()
                    : -1;

            private static async Task<int> RunAsync(int input)
            {
                Values.DisposeCount = 0;
                var result = 0;
                var selected = new Values(input);
                foreach (var outer in new Values(input))
                {
                    foreach (var value in selected)
                    {
                        if (value > outer)
                        {
                            continue;
                        }

                        await Task.CompletedTask.ConfigureAwait(false);
                        result += value;
                    }
                }

                return result * 100 + Values.DisposeCount;
            }
        }

        public sealed class Values(int count) : IEnumerable<int>
        {
            public static int DisposeCount;
            public IEnumerator<int> GetEnumerator() => new Enumerator(count);
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator(int count) : IEnumerator<int>
            {
                private int _current = -1;
                public int Current => _current;
                object IEnumerator.Current => Current;
                public bool MoveNext() => ++_current < count;
                public void Reset() => _current = -1;
                public void Dispose() => DisposeCount++;
            }
        }
        """;
}
