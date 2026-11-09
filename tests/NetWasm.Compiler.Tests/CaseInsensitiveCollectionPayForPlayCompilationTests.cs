using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CaseInsensitiveCollectionPayForPlayCompilationTests
{
    private static readonly string[] CultureMachinery =
    [
        "System.Globalization.CultureInfo",
        "System.Globalization.CultureData",
    ];

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void OrdinalIgnoreCaseCollectionsDoNotReachCultureMachinery(bool optimize, WasmTarget target)
    {
        // The string comparers' ordinal ignore-case equality calls the ordinal comparison directly: through
        // string.Equals(x, y, comparisonType) its culture-aware branches would make CultureInfo reachable.
        const string source = """
            using System;
            using System.Collections.Generic;
            namespace CaseInsensitiveCollectionFixture;
            public static class EntryPoint
            {
                public static int Run(int value)
                {
                    var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Key"] = 1, ["äb"] = 2 };
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Item" };
                    if (!map.TryGetValue("KEY", out var key) || key != 1) return 1;
                    if (!map.TryGetValue("ÄB", out var umlaut) || umlaut != 2) return 2;
                    if (map.ContainsKey("Keys") || !set.Contains("iTEM") || set.Add("ITEM")) return 3;
                    if (!StringComparer.OrdinalIgnoreCase.Equals("abc", "ABC") || StringComparer.OrdinalIgnoreCase.Equals("abc", null)) return 4;
                    return value + 1;
                }
            }
            """;
        using var assets = TestAssets.Create();
        var assembly = optimize
            ? assets.CompileOptimizedSource("CaseInsensitiveCollectionFixture", source)
            : assets.CompileSource("CaseInsensitiveCollectionFixture", source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "CaseInsensitiveCollectionFixture.EntryPoint", "Run",
            [new("run", "CaseInsensitiveCollectionFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/platform"));
        var reachable = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method.DeclaringType.FullName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(reachable, CultureMachinery.Contains);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }
}
