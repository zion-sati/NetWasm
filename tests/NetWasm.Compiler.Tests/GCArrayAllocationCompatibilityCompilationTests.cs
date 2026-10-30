using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class GCArrayAllocationCompatibilityCompilationTests
{
    [Fact]
    public void CompilerExecutesGcArrayAllocationContracts()
    {
        using var assets = TestAssets.Create();
        var coreLib = assets.CompileCoreLibVariant(
            "GCArrayAllocationCompatibilityCoreLib",
            string.Empty);
        var assembly = assets.CompileSourceAgainstCoreLib(
            "GCArrayAllocationCompatibilityFixture",
            Source,
            coreLib);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [coreLib],
            "GCArrayAllocationCompatibilityFixture.EntryPoint",
            "Run",
            [],
            WasmTarget.Wasm32));

        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 0));
    }

    private const string Source = """
using System;

namespace GCArrayAllocationCompatibilityFixture;

public sealed class Payload
{
    public int Value;
}

public static class EntryPoint
{
    public static int Run(int input)
    {
        var values = GC.AllocateArray<int>(3);
        if (values.Length != 3 || values[0] != 0 || values[1] != 0 || values[2] != 0)
        {
            return 1;
        }

        values[0] = 42;
        var references = GC.AllocateUninitializedArray<Payload>(2, pinned: false);
        if (references.Length != 2 || references[0] is not null || references[1] is not null)
        {
            return 2;
        }

        try
        {
            _ = GC.AllocateArray<int>(-1);
            return 3;
        }
        catch (OverflowException)
        {
        }

        // Objects never move, so a pinned array is an ordinary array that
        // keeps its contents across collections, whatever its element type.
        var pinned = GC.AllocateArray<int>(4, pinned: true);
        pinned[3] = 7;
        GC.Collect();
        if (pinned.Length != 4 || pinned[0] != 0 || pinned[3] != 7)
        {
            return 4;
        }

        var pinnedReferences = GC.AllocateUninitializedArray<Payload>(2, pinned: true);
        pinnedReferences[1] = new Payload { Value = 5 };
        GC.Collect();
        if (pinnedReferences[0] is not null || pinnedReferences[1].Value != 5 ||
            GC.AllocateUninitializedArray<byte>(3, pinned: true).Length != 3)
        {
            return 5;
        }

        return input;
    }
}
""";
}
