using Microsoft.Build.Framework;
using NetWasm.Sdk.Pack.Restore;
using Xunit;

namespace NetWasm.Sdk.Pack.Tests;

// NuGet restores a package with a package-wide build/ file but no netwasm0.1 library without its library; the task
// selects the nearest .NET Standard one, and leaves alone packages NuGet resolved, packages with no .NET Standard
// library and assets a direct reference excludes.
public sealed class ResolveStandardFallbackAssetsTaskTests
{
    [Fact]
    public void SelectsTheNearestStandardLibraryForPackagesRestoredWithoutOne()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-fallback-");
        try
        {
            var assets = Path.Combine(directory.FullName, "project.assets.json");
            File.WriteAllText(assets, """
                {
                  "targets": {
                    "netwasm0.1": {
                      "Logging/4.0.0": { "type": "package", "build": { "buildTransitive/Logging.targets": {} } },
                      "Contracts/1.0.0": { "type": "package" },
                      "Resolved/2.0.0": { "type": "package", "compile": { "lib/netstandard2.0/Resolved.dll": {} }, "runtime": { "lib/netstandard2.0/Resolved.dll": {} } },
                      "Desktop/3.0.0": { "type": "package" },
                      "Excluded/5.0.0": { "type": "package" },
                      "Local/1.0.0": { "type": "project" }
                    }
                  },
                  "libraries": {
                    "Logging/4.0.0": { "type": "package", "path": "logging/4.0.0", "files": [ "buildTransitive/Logging.targets", "lib/net8.0/Logging.dll", "lib/netstandard2.0/Logging.dll", "lib/netstandard2.0/Logging.xml", "lib/netstandard2.1/Logging.dll" ] },
                    "Contracts/1.0.0": { "type": "package", "path": "contracts/1.0.0", "files": [ "ref/netstandard2.0/Contracts.dll", "lib/netstandard2.0/Contracts.dll" ] },
                    "Resolved/2.0.0": { "type": "package", "path": "resolved/2.0.0", "files": [ "lib/netstandard2.0/Resolved.dll" ] },
                    "Desktop/3.0.0": { "type": "package", "path": "desktop/3.0.0", "files": [ "build/Desktop.props", "lib/net8.0/Desktop.dll" ] },
                    "Excluded/5.0.0": { "type": "package", "path": "excluded/5.0.0", "files": [ "lib/netstandard2.0/Excluded.dll" ] },
                    "Local/1.0.0": { "type": "project", "path": "../Local/Local.csproj" }
                  },
                  "packageFolders": { "/packages/": {} },
                  "project": { "frameworks": { "netwasm0.1": { "dependencies": { "Excluded": { "exclude": "Compile" } } } } }
                }
                """);
            var task = new ResolveStandardFallbackAssetsTask { ProjectAssetsFile = assets, TargetNames = ["netwasm0.1", "NetWasm,Version=v0.1"] };

            Assert.True(task.Execute());
            Assert.Equal(["contracts/1.0.0/ref/netstandard2.0/Contracts.dll", "logging/4.0.0/lib/netstandard2.1/Logging.dll"],
                task.CompileAssets.Select(Relative).Order(StringComparer.Ordinal));
            Assert.Equal(["contracts/1.0.0/lib/netstandard2.0/Contracts.dll", "excluded/5.0.0/lib/netstandard2.0/Excluded.dll", "logging/4.0.0/lib/netstandard2.1/Logging.dll"],
                task.RuntimeAssets.Select(Relative).Order(StringComparer.Ordinal));
            var runtime = task.RuntimeAssets.Single(item => item.GetMetadata("NuGetPackageId") == "Logging");
            Assert.Equal(("4.0.0", "lib/netstandard2.1/Logging.dll", "true", "Logging.dll"),
                (runtime.GetMetadata("NuGetPackageVersion"), runtime.GetMetadata("PathInPackage"), runtime.GetMetadata("CopyLocal"), runtime.GetMetadata("DestinationSubPath")));
            Assert.All(task.CompileAssets, item => Assert.Equal("false", item.GetMetadata("Private")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AMissingAssetsFileSelectsNothing()
    {
        var task = new ResolveStandardFallbackAssetsTask { ProjectAssetsFile = "/nonexistent/project.assets.json", TargetNames = ["netwasm0.1"] };
        Assert.True(task.Execute());
        Assert.Empty(task.CompileAssets);
    }

    private static string Relative(ITaskItem item) => Path.GetRelativePath("/packages", item.ItemSpec).Replace('\\', '/');
}
