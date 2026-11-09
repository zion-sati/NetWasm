using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace NetWasm.Hosting.Build.JavaScript;

/// <summary>
/// What a raw page or worker host bundles for one application: the platform
/// providers for the WASI interfaces its raw bindings require, the filesystem
/// scope only when it links a filesystem interface, and HTTP over fetch only
/// when it links wasi:http.
/// </summary>
internal sealed record RawBrowserHostCapabilities(
    bool LinksFilesystem,
    string ProviderCatalogJson)
{
    public bool LinksHttp { get; init; }

    private const string CatalogFileName = "platform-provider-catalog.generated.mjs";
    private const string CatalogPrefix = "export const platformProviderCatalog = deepFreeze(";
    private const string CatalogSuffix = ");\n";

    public static RawBrowserHostCapabilities Select(
        string hostingJavaScriptRoot,
        IEnumerable<string> requiredImportModules)
    {
        var modules = requiredImportModules
            .Where(module => module.StartsWith("wasi:", StringComparison.Ordinal))
            .ToImmutableSortedSet(StringComparer.Ordinal);
        return new(
            modules.Any(module => module.StartsWith("wasi:filesystem/", StringComparison.Ordinal)),
            SelectProviders(Path.Combine(hostingJavaScriptRoot, CatalogFileName), modules))
        {
            LinksHttp = modules.Any(module => module.StartsWith("wasi:http/", StringComparison.Ordinal)),
        };
    }

    // The shipped catalog module is the generator's JSON wrapped in deepFreeze,
    // so the build selects from exactly the catalog it bundles.
    private static string SelectProviders(string catalogPath, ImmutableSortedSet<string> modules)
    {
        var text = File.ReadAllText(catalogPath).ReplaceLineEndings("\n");
        var start = text.IndexOf(CatalogPrefix, StringComparison.Ordinal);
        if (start < 0 || !text.EndsWith(CatalogSuffix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The hosting platform provider catalog '{catalogPath}' has an unexpected format.");
        }
        var catalog = JsonNode.Parse(text[(start + CatalogPrefix.Length)..^CatalogSuffix.Length])!.AsObject();
        var selected = new JsonArray();
        var provided = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in catalog["providers"]!.AsArray())
        {
            var module = (string)provider!["module"]!;
            if (modules.Contains(module))
            {
                selected.Add(provider.DeepClone());
                provided.Add(module);
            }
        }
        var missing = modules.FirstOrDefault(module => !provided.Contains(module));
        if (missing is not null)
        {
            throw new ArgumentException($"Required WASI interface '{missing}' has no hosting platform provider.");
        }
        catalog["providers"] = selected;
        return catalog.ToJsonString();
    }
}
