using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ExceptionOnlyContinuationLoopCompilationTests
{
    [Fact]
    public void DesktopExecutesCatchFilterAndAsyncContinuationLoops()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "ExceptionOnlyContinuationLoops.Desktop",
            [CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Assert.True(compilation.Emit(stream).Success);
        stream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
        var run = assembly.GetType("ExceptionOnlyContinuationLoops.EntryPoint")!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

        Assert.Equal(3333, run.Invoke(null, [3]));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesCatchFilterAndAsyncContinuationLoops(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var executor = Assert.IsAssignableFrom<ICompilationScenarioExecutor>(new CompilationScenarioExecutor(assets));

        var result = executor.Execute(new CompilationScenario(
            "ExceptionOnlyContinuationLoops",
            Source,
            "ExceptionOnlyContinuationLoops.EntryPoint",
            optimize,
            target,
            Input: 3,
            ReferencePaths: ImmutableArray<string>.Empty)
        {
            Exports = [new RequestedExport("run", "ExceptionOnlyContinuationLoops.EntryPoint", "Run")],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/async-platform@1.0.0",
        });

        Assert.Equal(3333, result);
    }

    private const string Source = """
        using System;
        using System.Threading.Tasks;

        namespace ExceptionOnlyContinuationLoops;

        public static class EntryPoint
        {
            public static int Run(int count) =>
                CatchOnly(count) * 1000 + SharedContinuation(count, false) * 100 +
                FilterOnly(count) * 10 + AsyncCatchOnly(count).GetAwaiter().GetResult();

            private static int CatchOnly(int count)
            {
                try { throw new InvalidOperationException(); }
                catch (InvalidOperationException) { }
                var result = 0;
                for (var index = 0; index < count; index++) result += index;
                return result;
            }

            private static int SharedContinuation(int count, bool fail)
            {
                try { if (fail) throw new InvalidOperationException(); }
                catch (InvalidOperationException) { }
                var result = 0;
                for (var index = 0; index < count; index++) result += index;
                return result;
            }

            private static int FilterOnly(int count)
            {
                try { throw new InvalidOperationException(); }
                catch (Exception) when (count > 0) { }
                var result = 0;
                for (var index = 0; index < count; index++) result += index;
                return result;
            }

            private static async Task<int> AsyncCatchOnly(int count)
            {
                try
                {
                    await Task.CompletedTask;
                    throw new InvalidOperationException();
                }
                catch (InvalidOperationException) { }
                var result = 0;
                for (var index = 0; index < count; index++) result += index;
                return result;
            }
        }
        """;
}
