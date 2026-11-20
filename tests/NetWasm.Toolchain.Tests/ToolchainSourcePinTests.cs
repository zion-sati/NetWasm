// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace NetWasm.Toolchain.Tests;

// The toolchain manifest pins these packaged files by digest, and each is
// staged byte for byte from a repository source. A source edit without its
// pin passes every unit test and fails only a consumer build, so check them
// here.
public sealed class ToolchainSourcePinTests
{
    [Theory]
    [InlineData("hosting-bundle.command", "src/NetWasm.Toolchain/JavaScript/netwasm-bundle.mjs")]
    [InlineData("wasm-tools.command", "src/NetWasm.Toolchain/JavaScript/run-wasm-tools.mjs")]
    [InlineData("netwasm.raw-inspection.command", "src/NetWasm.Compiler.ComponentModel/Raw/JavaScript/run-raw-module-inspection.mjs")]
    [InlineData("netwasm.raw-inspection.command-core", "src/NetWasm.Compiler.ComponentModel/Raw/JavaScript/raw-module-inspection-command.mjs")]
    [InlineData("netwasm.raw-inspection.error", "src/NetWasm.Compiler.ComponentModel/Raw/JavaScript/raw-module-inspection-error.mjs")]
    [InlineData("netwasm.raw-inspection.inspector", "src/NetWasm.Compiler.ComponentModel/Raw/JavaScript/raw-module-import-inspector.mjs")]
    [InlineData("netwasm.raw-inspection.core-reader", "src/NetWasm.Compiler.ComponentModel/Raw/JavaScript/core-module-import-reader.mjs")]
    [InlineData("netwasm.raw-inspection.binaryen-reader", "src/NetWasm.Compiler.ComponentModel/Raw/JavaScript/binaryen-function-import-reader.mjs")]
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

    // Every file the package takes as it is from a repository source (a None item with its own PackagePath) and the
    // manifest pins. A notice added to such sources once left seven pins stale that the cases above do not name.
    [Fact]
    public void EveryPinnedFilePackedFromASourceMatchesIt()
    {
        var root = RepositoryRoot();
        var project = Path.Combine(root, "src", "NetWasm.Toolchain", "NetWasm.Toolchain.csproj");
        var sources = XDocument.Load(project).Descendants("None")
            .Select(item => (Include: (string?)item.Attribute("Include"), PackagePath: (string?)item.Attribute("PackagePath")))
            .Where(item => item.Include is { } include && item.PackagePath is { } packagePath &&
                include.IndexOfAny(['$', '*', ';']) < 0 && !packagePath.EndsWith('/'))
            .ToDictionary(item => item.PackagePath!, item => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, item.Include!)));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(root, "src", "NetWasm.Toolchain", "toolchain-manifest.json")));

        var pins = PinnedFiles(manifest.RootElement)
            .Where(pin => sources.ContainsKey(pin.RelativePath))
            .Select(pin => (pin.RelativePath, pin.Sha256,
                Actual: Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(sources[pin.RelativePath])))))
            .ToArray();

        Assert.NotEmpty(pins);
        Assert.All(pins, pin => Assert.True(pin.Sha256 == pin.Actual,
            $"{pin.RelativePath} is pinned at {pin.Sha256}; its source hashes to {pin.Actual}."));
    }

    private static IEnumerable<(string RelativePath, string Sha256)> PinnedFiles(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("relativePath", out var path) && element.TryGetProperty("sha256", out var digest))
            {
                yield return (path.GetString()!, digest.GetString()!);
            }
            foreach (var property in element.EnumerateObject())
            {
                foreach (var pin in PinnedFiles(property.Value)) yield return pin;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var pin in PinnedFiles(item)) yield return pin;
            }
        }
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
