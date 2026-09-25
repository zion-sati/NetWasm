using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record CorpusCaseBinding(string CaseId, string TestMethod, CorpusInputKind InputKind);

internal sealed record CorpusCaseCatalog(ImmutableDictionary<string, CorpusCaseManifest> Cases);

internal interface ICorpusCaseCatalogBuilder
{
    CorpusCaseCatalog Build(CorpusCaseAssets assets, ImmutableArray<CorpusCaseBinding> bindings);
}

internal sealed class CorpusCaseCatalogBuilder(
    ICorpusCaseManifestParser parser,
    ICorpusCaseManifestVerifier verifier) : ICorpusCaseCatalogBuilder
{
    public CorpusCaseCatalog Build(CorpusCaseAssets assets, ImmutableArray<CorpusCaseBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (assets.Manifests.IsDefaultOrEmpty || assets.SourceFiles.IsDefault || bindings.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A catalog requires explicit assets and nonempty case bindings.");
        }
        var owners = new Dictionary<string, CorpusCaseBinding>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            if (binding is null || string.IsNullOrWhiteSpace(binding.CaseId) || !owners.TryAdd(binding.CaseId, binding))
            {
                throw new ArgumentException("Case bindings must have distinct nonempty IDs.", nameof(bindings));
            }
        }
        var availableSources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in assets.SourceFiles)
        {
            if (string.IsNullOrWhiteSpace(source) || !availableSources.Add(source))
            {
                throw new ArgumentException("Source assets must have distinct nonempty names.", nameof(assets));
            }
        }
        var usedSources = new HashSet<string>(StringComparer.Ordinal);
        var manifestNames = new HashSet<string>(StringComparer.Ordinal);
        var cases = ImmutableDictionary.CreateBuilder<string, CorpusCaseManifest>(StringComparer.Ordinal);
        foreach (var asset in assets.Manifests)
        {
            if (asset is null || string.IsNullOrWhiteSpace(asset.Name) || !manifestNames.Add(asset.Name))
            {
                throw new ArgumentException("Manifest assets must have distinct nonempty names.", nameof(assets));
            }
            var manifest = parser.Parse(asset.Json);
            verifier.Verify(manifest);
            if (!cases.TryAdd(manifest.CaseId, manifest))
            {
                throw new ArgumentException("Duplicate case declaration.", nameof(assets));
            }
            if (!owners.TryGetValue(manifest.CaseId, out var owner) ||
                owner.TestMethod != manifest.TestMethod || owner.InputKind != manifest.InputKind)
            {
                throw new ArgumentException("Case declaration has no matching test binding.", nameof(bindings));
            }
            foreach (var source in manifest.SourceFiles)
            {
                if (!availableSources.Contains(source))
                {
                    throw new ArgumentException("Declared source asset is missing.", nameof(assets));
                }
                usedSources.Add(source);
            }
        }
        if (owners.Count != cases.Count || !availableSources.SetEquals(usedSources))
        {
            throw new ArgumentException("The catalog has an orphan test binding or source asset.");
        }
        return new(cases.ToImmutable());
    }
}
