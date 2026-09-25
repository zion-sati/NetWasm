using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class LinkedCorpusObservationRequestFactoryTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, "wasm32")]
    [InlineData(WasmTarget.Wasm64, "wasm64")]
    public void CreatePreservesArtifactIdentityInputsAndTraceContract(WasmTarget target, string targetName)
    {
        var fixture = new CorpusFixture("Fixture", "Tests", "", [-1, 0, 1])
        {
            ExposesLegacyTrace = false,
            UsesTypedTrace = true,
        };
        var factory = Assert.IsAssignableFrom<ILinkedCorpusObservationRequestFactory>(
            new LinkedCorpusObservationRequestFactory());

        var request = factory.Create(fixture, target, "module", "manifest",
            new string('a', 64), new string('f', 64));

        Assert.Equal(1, request.SchemaVersion);
        Assert.Equal(targetName, request.Target);
        Assert.Equal("module", request.ModulePath);
        Assert.Equal("manifest", request.ManifestPath);
        Assert.Equal(new string('a', 64), request.ModuleSha256);
        Assert.Equal(new string('f', 64), request.ManifestSha256);
        Assert.Equal(fixture.Inputs, request.Inputs);
        Assert.False(request.ExposesLegacyTrace);
        Assert.True(request.UsesTypedTrace);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void CreateRejectsInvalidContracts(int invalid)
    {
        var factory = new LinkedCorpusObservationRequestFactory();
        var fixture = new CorpusFixture("Fixture", "Tests", "", [0]);
        var modulePath = "module";
        var manifestPath = "manifest";
        var moduleHash = new string('a', 64);
        var manifestHash = new string('b', 64);
        if (invalid == 0) fixture = null!;
        else if (invalid == 1) modulePath = " ";
        else if (invalid == 2) manifestPath = " ";
        else if (invalid == 3) moduleHash = new string('A', 64);
        else if (invalid == 4) manifestHash = "short";
        else if (invalid == 5) fixture = fixture with { Inputs = [] };
        else if (invalid == 6) fixture = fixture with { Inputs = [0, 0] };

        Assert.ThrowsAny<ArgumentException>(() => factory.Create(
            fixture, invalid == 7 ? (WasmTarget)99 : WasmTarget.Wasm32,
            modulePath, manifestPath, moduleHash, manifestHash));
    }

    [Theory]
    [InlineData("g")]
    [InlineData("/")]
    public void CreateRejectsNonHexHashCharacters(string character)
    {
        var fixture = new CorpusFixture("Fixture", "Tests", "", [0]);
        Assert.Throws<ArgumentException>(() => new LinkedCorpusObservationRequestFactory().Create(
            fixture, WasmTarget.Wasm32, "module", "manifest",
            character + new string('a', 63), new string('b', 64)));
    }
}
