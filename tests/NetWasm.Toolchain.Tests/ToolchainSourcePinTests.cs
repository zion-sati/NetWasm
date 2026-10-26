using System.Security.Cryptography;
using System.Text.Json;

namespace NetWasm.Toolchain.Tests;

// The toolchain manifest pins these packaged files by digest, and each is
// staged byte for byte from a repository source. A source edit without its
// pin passes every unit test and fails only a consumer build, so check them
// here.
public sealed class ToolchainSourcePinTests
{
    [Theory]
    [InlineData("hosting-bundle.command", "src/NetWasm.Toolchain/JavaScript/netwasm-bundle.mjs")]
    [InlineData("hosting-bundle.closure-policy", "eng/hosting-bundle-closure.json")]
    [InlineData("jco.closure-policy", "eng/jco-transpile-closure.json")]
    public void PinnedAssetsMatchTheirSources(string assetId, string source)
    {
        var root = RepositoryRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(root, "src", "NetWasm.Toolchain", "toolchain-manifest.json")));
        var pinned = FindAsset(manifest.RootElement, assetId).GetProperty("sha256").GetString();

        var actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, source))));

        Assert.Equal(pinned, actual);
    }

    private static JsonElement FindAsset(JsonElement element, string assetId)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                && id.GetString() == assetId && element.TryGetProperty("sha256", out _))
            {
                return element;
            }
            foreach (var property in element.EnumerateObject())
            {
                if (TryFind(property.Value, assetId, out var found)) return found;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFind(item, assetId, out var found)) return found;
            }
        }
        throw new InvalidOperationException($"The toolchain manifest has no asset '{assetId}'.");
    }

    private static bool TryFind(JsonElement element, string assetId, out JsonElement found)
    {
        try
        {
            found = FindAsset(element, assetId);
            return true;
        }
        catch (InvalidOperationException)
        {
            found = default;
            return false;
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "NetWasm.Toolchain", "toolchain-manifest.json")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("The source pin test requires the source checkout.");
    }
}
