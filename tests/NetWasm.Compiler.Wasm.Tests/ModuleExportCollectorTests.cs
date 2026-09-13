using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ModuleExportCollectorTests
{
    [Theory]
    [InlineData(WasmEntryPointProfile.Process, true)]
    [InlineData(WasmEntryPointProfile.Internal, false)]
    public void CollectsRuntimeAndNamedExportsInDeterministicOrder(
        WasmEntryPointProfile entryPointProfile,
        bool includesRun)
    {
        var exports = CreateCollector().Collect(
            entryPointProfile,
            3,
            4,
            5,
            new Dictionary<string, int> { ["z"] = 9, ["a"] = 8 },
            new Dictionary<string, int> { ["callback"] = 10 },
            new Dictionary<string, int> { ["resolve"] = 11 },
            new Dictionary<string, int> { ["status"] = 12 });

        Assert.Equal(includesRun, exports.Any(item => item.Name == "run"));
        Assert.Contains(exports, item =>
            item.Name == RuntimeAbi.ManagedFilterDispatcher && item.Index == 4);
        Assert.Contains(exports, item =>
            item.Name == RuntimeAbi.ManagedFinalizerDispatcher && item.Index == 5);
        Assert.True(exports.IndexOf("a") < exports.IndexOf("z"));
        Assert.Equal(8, exports.Single(item => item.Name == "a").Index);
    }

    [Fact]
    public void RejectsMissingCollectionsAndNegativeIndices()
    {
        var collector = CreateCollector();
        IReadOnlyDictionary<string, int> empty = new Dictionary<string, int>();

        Assert.Throws<ArgumentOutOfRangeException>(() => collector.Collect(
            WasmEntryPointProfile.Process, -1, 0, 0, empty, empty, empty, empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => collector.Collect(
            WasmEntryPointProfile.Process, 0, -1, 0, empty, empty, empty, empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => collector.Collect(
            WasmEntryPointProfile.Process, 0, 0, -1, empty, empty, empty, empty));
        Assert.Throws<ArgumentNullException>(() => collector.Collect(
            WasmEntryPointProfile.Process, 0, 0, 0, null!, empty, empty, empty));
        Assert.Throws<ArgumentNullException>(() => collector.Collect(
            WasmEntryPointProfile.Process, 0, 0, 0, empty, null!, empty, empty));
        Assert.Throws<ArgumentNullException>(() => collector.Collect(
            WasmEntryPointProfile.Process, 0, 0, 0, empty, empty, null!, empty));
        Assert.Throws<ArgumentNullException>(() => collector.Collect(
            WasmEntryPointProfile.Process, 0, 0, 0, empty, empty, empty, null!));
    }

    private static IModuleExportCollector CreateCollector() =>
        new[] { new ModuleExportCollector() }
            .Cast<IModuleExportCollector>()
            .Single();
}

internal static class WasmExportTestExtensions
{
    public static int IndexOf(this IReadOnlyList<WasmExport> exports, string name)
    {
        for (var index = 0; index < exports.Count; index++)
        {
            if (exports[index].Name == name)
            {
                return index;
            }
        }
        return -1;
    }
}
