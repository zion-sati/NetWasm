using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeValidationProfileTests
{
    // A plain core module's runtime keeps only the runtime's own imports, so a WASI or Component
    // Model import is undeclared and the required environment import is no longer required.
    [Fact]
    public void WithoutWasiKeepsOnlyTheRuntimesOwnImports()
    {
        var profile = RuntimePackTestData.NativeProfile() with
        {
            Imports =
            [
                new("netwasm.application.v1", "netwasm.filter", [0x7f], [0x7f], true),
                new("env", "emscripten_notify_memory_growth", [0x7f], [], true),
                new("cm32p2|wasi:cli/environment@0.3", "get-environment", [0x7f], [], true),
                new("wasi:cli/stdout@0.3.2", "[stream-new-0]write-via-stream", [], [0x7e], false),
                new("$root", "[waitable-set-new]", [], [0x7f], false),
            ],
        };

        var stripped = profile.WithoutWasi();

        Assert.Equal(["netwasm.application.v1", "env"], stripped.Imports.Select(import => import.Module));
        Assert.Equal(profile.Features, stripped.Features);
        Assert.Equal(profile.Version, stripped.Version);
    }
}
