using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record CorpusManifestAsset(string Name, string Json);

internal sealed record CorpusCaseAssets(
    CorpusFeatureCatalog Features,
    ImmutableArray<CorpusManifestAsset> Manifests,
    ImmutableArray<string> SourceFiles);

internal interface ICorpusCaseAssetsReader
{
    CorpusCaseAssets Read();
}

internal sealed class EmbeddedCorpusCaseAssetsReader(Assembly assembly) : ICorpusCaseAssetsReader
{
    private const string Prefix = "Correctness/Fixtures/";

    public CorpusCaseAssets Read()
    {
        using var inventory = assembly.GetManifestResourceStream("Correctness/cil-coverage-inventory.json")
            ?? throw new InvalidOperationException("The semantic inventory resource is missing.");
        using var document = JsonDocument.Parse(inventory);
        var features = new CorpusFeatureCatalog(document.RootElement.GetProperty("semanticFamilies")
            .EnumerateArray().Select(item => item.GetProperty("id").GetString()!)
            .ToDictionary(id => id, StringComparer.Ordinal).Keys
            .ToImmutableHashSet(StringComparer.Ordinal));
        var manifests = ImmutableArray.CreateBuilder<CorpusManifestAsset>();
        var sources = ImmutableArray.CreateBuilder<string>();
        foreach (var resource in assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            if (!resource.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }
            var name = resource[Prefix.Length..];
            if (name.EndsWith(".case.json", StringComparison.Ordinal))
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                manifests.Add(new(name, reader.ReadToEnd()));
            }
            else if (name.EndsWith(".cs", StringComparison.Ordinal) || name.EndsWith(".cs.txt", StringComparison.Ordinal))
            {
                sources.Add(name);
            }
            else
            {
                throw new InvalidOperationException("Unknown embedded corpus asset kind.");
            }
        }
        return new(features, manifests.ToImmutable(), sources.ToImmutable());
    }
}
