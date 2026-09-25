using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ILinkedCorpusObservationRequestFactory
{
    LinkedCorpusObservationRequest Create(
        CorpusFixture fixture,
        WasmTarget target,
        string modulePath,
        string manifestPath,
        string moduleSha256,
        string manifestSha256);
}

internal sealed class LinkedCorpusObservationRequestFactory : ILinkedCorpusObservationRequestFactory
{
    public LinkedCorpusObservationRequest Create(
        CorpusFixture fixture,
        WasmTarget target,
        string modulePath,
        string manifestPath,
        string moduleSha256,
        string manifestSha256)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        if (fixture.Inputs.IsDefaultOrEmpty || fixture.Inputs.Distinct().Count() != fixture.Inputs.Length)
        {
            throw new ArgumentException("linked corpus inputs must be nonempty and unique", nameof(fixture));
        }
        if (!IsSha256(moduleSha256) || !IsSha256(manifestSha256))
        {
            throw new ArgumentException("linked corpus artifact identities must be lowercase SHA-256", nameof(moduleSha256));
        }
        var targetName = target switch
        {
            WasmTarget.Wasm32 => "wasm32",
            WasmTarget.Wasm64 => "wasm64",
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        return new(1, targetName, modulePath, manifestPath, moduleSha256,
            manifestSha256, fixture.Inputs, fixture.ExposesLegacyTrace,
            fixture.UsesTypedTrace);
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
