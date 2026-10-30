using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class AsyncLoopExceptionOwnershipCompilationTests
{
    [Fact]
    public void DesktopExecutesAwaitInsideFilteredLoopWithCatchReturn()
    {
        var compilation = CSharpCompilation.Create(
            "AsyncLoopExceptionOwnership.Desktop",
            [CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.Preview))],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assemblyStream = new MemoryStream();
        var emit = compilation.Emit(assemblyStream);
        Assert.True(emit.Success);

        assemblyStream.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(assemblyStream);
        var run = assembly.GetType("AsyncLoopExceptionOwnership.Application.EntryPoint")!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

        Assert.Equal(122_202, run.Invoke(null, [2]));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesAwaitInsideFilteredLoopWithCatchReturn(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var result = executor.Execute(new CompilationScenario(
            "AsyncLoopExceptionOwnership.Application",
            Source,
            "AsyncLoopExceptionOwnership.Application.EntryPoint",
            optimize,
            target,
            Input: 2,
            ReferencePaths: ImmutableArray<string>.Empty)
        {
            Exports =
            [
                new RequestedExport(
                    "run",
                    "AsyncLoopExceptionOwnership.Application.EntryPoint",
                    "Run"),
            ],
            WitPath = Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld = "netwasm:platform/async-platform@1.0.0",
        });

        Assert.Equal(122_202, result);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

    private const string Source = """
        using System;
        using System.Threading.Tasks;

        namespace AsyncLoopExceptionOwnership.Application;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var scanned = ScanAsync(input).GetAwaiter().GetResult();
                var stopped = StopAsync(input).GetAwaiter().GetResult();
                var continued = ContinueAsync(input).GetAwaiter().GetResult();
                return scanned * 10_000 + stopped * 100 + continued;
            }

            private static async ValueTask<int> ScanAsync(int input)
            {
                foreach (var value in new[] { 0, 1, 2 })
                {
                    if (value != input)
                    {
                        continue;
                    }

                    try
                    {
                        await CompleteAsync();
                    }
                    catch (Exception)
                    {
                        return -1;
                    }
                }

                return input + 10;
            }

            private static async ValueTask<int> StopAsync(int input)
            {
                foreach (var value in new[] { 0, 1, 2 })
                {
                    try
                    {
                        await CompleteAsync();
                        if (value == input)
                        {
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        return -1;
                    }
                }

                return input + 20;
            }

            private static async ValueTask<int> ContinueAsync(int input)
            {
                var result = 0;
                foreach (var value in new[] { 0, 1, 2 })
                {
                    try
                    {
                        await CompleteAsync();
                        if (value != input)
                        {
                            continue;
                        }
                    }
                    catch (Exception)
                    {
                        return -1;
                    }

                    result += value;
                }

                return result;
            }

            private static async ValueTask CompleteAsync() =>
                await Task.CompletedTask;
        }
        """;
}
