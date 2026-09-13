using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class EmissionPlanningModelTests
{
    [Fact]
    public void RequestAndEmissionRecordsExposeTheirCompleteImmutableData()
    {
        var request = CreateEmissionRequest();
        var callbacks = ImmutableDictionary<
            (EntityKey Method, int ParameterIndex), HostCallbackDeclaration>.Empty;
        var imports = new ModuleImportCollectionRequest(
            request,
            [],
            [],
            WasmTarget.Wasm64,
            callbacks);
        var emission = new ManagedMethodBodyEmission(
            [0xaa],
            1,
            2,
            3,
            "method",
            FilterEnvironmentLayout.Empty,
            ImmutableDictionary<int, int>.Empty);
        var record = new ManagedMethodEmissionRecord("identity", "method", emission);

        Assert.Same(request, imports.Emission);
        Assert.Equal(WasmTarget.Wasm64, imports.Target);
        Assert.Same(callbacks, imports.HostCallbacks);
        Assert.Equal("identity", record.Identity);
        Assert.Equal("method", record.MethodKey);
        Assert.Same(emission, record.Emission);
    }
}
