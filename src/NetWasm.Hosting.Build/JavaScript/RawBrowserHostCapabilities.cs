using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace NetWasm.Hosting.Build.JavaScript;

/// <summary>
/// What a raw page or worker host bundles for one application: the Preview 2
/// shim families and platform providers for the WASI interfaces its raw
/// bindings require, and nothing else. A one-export page that links no
/// filesystem, network or clock interface gets no shims or providers for them.
/// </summary>
internal sealed record RawBrowserHostCapabilities(
    ImmutableArray<string> ShimFamilies,
    string ProviderCatalogJson)
{
    private const string CatalogFileName = "platform-provider-catalog.generated.mjs";
    private const string CatalogPrefix = "export const platformProviderCatalog = deepFreeze(";
    private const string CatalogSuffix = ");\n";

    // preview2-shim's browser families, keyed by WASI package name. cli and io
    // are always present: NetWasm writes standard output through io's streams.
    private static readonly ImmutableHashSet<string> KnownFamilies =
        ["cli", "clocks", "filesystem", "http", "io", "random", "sockets"];

    public bool LinksFilesystem => ShimFamilies.Contains("filesystem");

    public static RawBrowserHostCapabilities Select(
        string hostingJavaScriptRoot,
        IEnumerable<string> requiredImportModules)
    {
        var modules = requiredImportModules
            .Where(module => module.StartsWith("wasi:", StringComparison.Ordinal))
            .ToImmutableSortedSet(StringComparer.Ordinal);
        var families = new SortedSet<string>(StringComparer.Ordinal) { "cli", "io" };
        foreach (var module in modules)
        {
            families.Add(Family(module));
        }
        return new(
            [.. families],
            SelectProviders(Path.Combine(hostingJavaScriptRoot, CatalogFileName), modules));
    }

    private static string Family(string module)
    {
        var slash = module.IndexOf('/', StringComparison.Ordinal);
        var family = slash < 0 ? string.Empty : module["wasi:".Length..slash];
        return KnownFamilies.Contains(family)
            ? family
            : throw new ArgumentException($"WASI interface '{module}' has no browser shim.");
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
