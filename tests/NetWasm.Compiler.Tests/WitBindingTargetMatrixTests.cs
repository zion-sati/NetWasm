using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

public sealed class WitBindingTargetMatrixTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void GeneratedManagedTypesExecuteForEveryProfile(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var document = Document();
        var generated = WitBindingCompositionRoot.Create()
            .Generate(document, document.Worlds[0]);
        var source = generated +
            """

            public static class EntryPoint
            {
                public static int Run(int input) => new Point(input + 1).X;
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("WitBindingTargetMatrixFixture", source)
            : assets.CompileSource("WitBindingTargetMatrixFixture", source);
        var identity = SHA256.HashData(File.ReadAllBytes(assembly));

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "NetWasm.Wit.Example.Types._1._0._0.EntryPoint",
            "Run",
            [],
            target));

        Assert.Equal(identity, SHA256.HashData(File.ReadAllBytes(assembly)));
        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            41,
            target,
            result.StaticDataEnd));
    }

    private static WitDocument Document()
    {
        using var json = JsonDocument.Parse(
            """{"record":{"fields":[{"name":"x","type":"s32"}]}}""");
        var point = new WitTypeDefinition(
            0,
            "point",
            json.RootElement.Clone(),
            0);
        var @interface = new WitInterface(
            0,
            "types",
            "example:types@1.0.0",
            ImmutableDictionary<string, int>.Empty.Add("point", 0),
            []);
        var world = new WitWorld(
            0,
            "types",
            "example:types@1.0.0",
            [new WitWorldItem("types", 0, null)],
            []);
        return new WitDocument(
            [new WitPackage(
                0,
                "example:types@1.0.0",
                ImmutableDictionary<string, int>.Empty.Add("types", 0),
                ImmutableDictionary<string, int>.Empty.Add("types", 0))],
            [@interface],
            [world],
            [point],
            "{}");
    }
}
