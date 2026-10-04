using Microsoft.Build.Framework;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.Integration;

// Maintainer-only task composition. The production task owns metadata encoding;
// the substituted materializer avoids native tools in this MSBuild boundary test.
public sealed class NativeRuntimeMetadataProducerTask : Microsoft.Build.Utilities.Task
{
    public string Variant { get; set; } = "first";

    [Output]
    public ITaskItem[] RuntimeModules { get; private set; } = [];

    public override bool Execute()
    {
        var task = new RuntimeMaterializationTask(new FixedMaterializer(Variant), new NativeLibraryItemReader())
        {
            BuildEngine = BuildEngine,
        };
        var succeeded = task.Execute();
        RuntimeModules = task.RuntimeModules;
        return succeeded;
    }

    private sealed class FixedMaterializer(string variant) : IRuntimeModuleMaterializer
    {
        public RuntimeMaterialization Materialize(RuntimeMaterializationRequest request) => new(
            "wasm32", "runtime.wasm", "same-digest", "netwasm.runtime.v1", "same-build", "same-tools",
            16, 65_536, 131_072, 2_147_483_648,
            new("runtime-materialization", "same-key", RuntimeMaterializationCacheOutcome.Hit, false, 42, 0, 0))
        {
            InternalRuntimeExports = [new(variant + "%3Bentry", 0), new("global;" + variant + "%253B", 3)],
        };
    }
}
