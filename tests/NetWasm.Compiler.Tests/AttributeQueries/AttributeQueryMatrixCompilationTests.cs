// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.AttributeQueries;

// Every attribute query of every target, filter shape, inheritance choice and overload answers as desktop .NET 11
// answers it: the same attributes in the same array type, the same Boolean, the same exception. By target group: types
// (one deriving from another, and a Serializable one), the assembly, the module.
public sealed class AttributeQueryMatrixCompilationTests
{
    [Theory]
    [InlineData("types", WasmTarget.Wasm32)]
    [InlineData("types", WasmTarget.Wasm64)]
    [InlineData("assembly", WasmTarget.Wasm32)]
    [InlineData("assembly", WasmTarget.Wasm64)]
    [InlineData("module", WasmTarget.Wasm32)]
    [InlineData("module", WasmTarget.Wasm64)]
    public void EveryQueryAnswersAsDesktopAnswers(string group, WasmTarget target)
    {
        var queries = AttributeQueryMatrix.Queries(group);
        var source = AttributeQueryMatrix.Source(queries);
        var expected = InProcessDesktopResults.Of(source, "AttributeQueryMatrix.Desktop");
        Assert.Equal(queries.Count, expected.Length);

        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource($"AttributeQueryMatrix{group}{target}", source);
        var compilation = NetWasmCompiler.Compile(CompilerTestSupport.CreateReactorOptions(
            assets, assembly, "EntryPoint", "Run", target));
        var actual = CompilerTestSupport.ExecuteWithStandardWasiNodeCapturingOutput(
                compilation.ApplicationModule, assets.Directory, 0, target, compilation.StaticDataEnd)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();
        Assert.Equal(queries.Count, actual.Length);

        var differences = Enumerable.Range(0, queries.Count)
            .Where(index => actual[index] != expected[index])
            .Select(index => $"{queries[index].Name}: {actual[index]}, desktop {expected[index]}")
            .ToList();
        Assert.True(differences.Count == 0,
            $"{differences.Count} of {queries.Count} queries differ from desktop " +
            $"({differences.Count(difference => difference.Contains(": -3,", StringComparison.Ordinal))} platform fallbacks):\n" +
            string.Join("\n", differences));
    }
}
