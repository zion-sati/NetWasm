using System.Xml.Linq;

namespace NetWasm.Toolchain.Tests;

public sealed class BrowserToolPackageAssetTests
{
    private static readonly string[] ReviewedBrowserModules =
    [
        "binaryen-host.mjs",
        "tool-inputs.mjs",
        "wasm-tools-host.mjs",
        "wasm32-memory-ceiling.mjs"
    ];

    [Fact]
    public void ToolchainPackageCarriesTheReviewedBrowserHostModules()
    {
        var repositoryRoot = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(repositoryRoot,
            "src/NetWasm.Toolchain/NetWasm.Toolchain.csproj"));
        var browserItems = project.Descendants("None")
            .Where(item => ((string?)item.Attribute("PackagePath"))?.StartsWith(
                "tools/netwasm/browser/", StringComparison.Ordinal) == true)
            .ToArray();

        var packagedModules = browserItems
            .Where(item => (string?)item.Attribute("Pack") == "true")
            .SelectMany(item => ((string?)item.Attribute("Include") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Where(path => path.EndsWith(".mjs", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ReviewedBrowserModules, packagedModules);
        Assert.DoesNotContain(packagedModules,
            module => module.EndsWith(".test.mjs", StringComparison.Ordinal));
        Assert.Contains(browserItems, item =>
            (string?)item.Attribute("Include") == "Browser/Tools/README.md" &&
            (string?)item.Attribute("Pack") == "true");

        foreach (var module in new[]
                 {
                     "binaryen-host.mjs",
                     "tool-inputs.mjs",
                     "wasm-tools-host.mjs",
                     "wasm32-memory-ceiling.mjs"
                 })
        {
            Assert.True(File.Exists(Path.Combine(repositoryRoot,
                "src/NetWasm.Toolchain/Browser/Tools", module)), module);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "NetWasm.slnx")))
        {
            directory = Directory.GetParent(directory)?.FullName
                ?? throw new InvalidOperationException("Repository root was not found.");
        }

        return directory;
    }
}
