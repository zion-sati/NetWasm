using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NetWasm.Sdk.Pack.Restore;

/// <summary>
/// Completes NuGet's netstandard2.1 asset fallback for netwasm0.1. NuGet uses an AssetTargetFallback framework only for
/// a package with no asset compatible with the project's own framework, and a package-wide build/ or buildTransitive/
/// file (Serilog's, Humanizer's, StackExchange.Redis's) is compatible with every framework, so such a package restores
/// with its dependencies but no library. For each restored package whose compile and runtime assets are both absent,
/// this selects the nearest .NET Standard library, as the fallback would have: ref/ for compilation where the package
/// has one, lib/ otherwise, and lib/ at run time. The items carry ResolvePackageAssets' metadata.
/// </summary>
public sealed class ResolveStandardFallbackAssetsTask : MsBuildTask
{
    private static readonly string[] Standards =
    [
        "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.5", "netstandard1.4", "netstandard1.3",
        "netstandard1.2", "netstandard1.1", "netstandard1.0",
    ];

    [Required] public string ProjectAssetsFile { get; set; } = "";

    /// <summary>The target's names in the assets file: the framework alias and the moniker.</summary>
    [Required] public string[] TargetNames { get; set; } = [];

    [Output] public ITaskItem[] CompileAssets { get; private set; } = [];

    [Output] public ITaskItem[] RuntimeAssets { get; private set; } = [];

    public override bool Execute()
    {
        CompileAssets = [];
        RuntimeAssets = [];
        if (!File.Exists(ProjectAssetsFile)) return true;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(ProjectAssetsFile));
            var root = document.RootElement;
            if (!root.TryGetProperty("targets", out var targets) || !root.TryGetProperty("libraries", out var libraries) ||
                !root.TryGetProperty("packageFolders", out var folders)) return true;
            var target = targets.EnumerateObject().FirstOrDefault(entry => TargetNames.Contains(entry.Name, StringComparer.OrdinalIgnoreCase));
            var packageFolder = folders.EnumerateObject().Select(entry => entry.Name).FirstOrDefault();
            if (target.Value.ValueKind != JsonValueKind.Object || packageFolder is null) return true;
            // A direct reference's IncludeAssets and ExcludeAssets, which the fallback honours as NuGet would.
            var references = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("project", out var project) && project.TryGetProperty("frameworks", out var frameworks))
                foreach (var framework in frameworks.EnumerateObject().Where(entry => TargetNames.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)))
                    if (framework.Value.TryGetProperty("dependencies", out var dependencies))
                        foreach (var dependency in dependencies.EnumerateObject())
                            references[dependency.Name] = dependency.Value;
            bool Allows(string id, string asset) =>
                !references.TryGetValue(id, out var reference) ||
                (!reference.TryGetProperty("include", out var include) || Flags(include).Contains(asset) || Flags(include).Contains("All")) &&
                (!reference.TryGetProperty("exclude", out var exclude) || !Flags(exclude).Contains(asset) && !Flags(exclude).Contains("All"));
            var compile = new List<ITaskItem>();
            var runtime = new List<ITaskItem>();
            foreach (var package in target.Value.EnumerateObject())
            {
                if (!package.Value.TryGetProperty("type", out var type) || type.GetString() != "package" ||
                    package.Value.TryGetProperty("compile", out _) || package.Value.TryGetProperty("runtime", out _) ||
                    !libraries.TryGetProperty(package.Name, out var library) ||
                    !library.TryGetProperty("path", out var path) || !library.TryGetProperty("files", out var files)) continue;
                var paths = files.EnumerateArray().Select(file => file.GetString() ?? "").ToArray();
                var separator = package.Name.IndexOf('/');
                var (id, version) = (package.Name[..separator], package.Name[(separator + 1)..]);
                var directory = Path.Combine(packageFolder, path.GetString() ?? "");
                var lib = Nearest(paths, "lib");
                if (Allows(id, "Compile"))
                    foreach (var asset in Nearest(paths, "ref") is { Length: > 0 } reference ? reference : lib)
                        compile.Add(Item(directory, asset, id, version, runtime: false));
                if (Allows(id, "Runtime"))
                    foreach (var asset in lib)
                        runtime.Add(Item(directory, asset, id, version, runtime: true));
            }
            CompileAssets = [.. compile];
            RuntimeAssets = [.. runtime];
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.LogError(null, "NWSDK050", null, null, 0, 0, 0, 0,
                "Could not read the restored assets for the netstandard2.1 fallback: {0}", error.Message);
            return false;
        }
    }

    private static string[] Flags(JsonElement value) =>
        (value.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // The assemblies in the nearest .NET Standard folder of ref/ or lib/.
    private static string[] Nearest(string[] files, string group)
    {
        foreach (var standard in Standards)
        {
            var prefix = group + "/" + standard + "/";
            var assets = files.Where(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                file.IndexOf('/', prefix.Length) < 0 &&
                file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (assets.Length != 0) return assets;
        }
        return [];
    }

    private static TaskItem Item(string directory, string asset, string id, string version, bool runtime)
    {
        var full = Path.GetFullPath(Path.Combine(directory, asset));
        var item = new TaskItem(full);
        item.SetMetadata("NuGetPackageId", id);
        item.SetMetadata("NuGetPackageVersion", version);
        item.SetMetadata("PathInPackage", asset);
        if (runtime)
        {
            item.SetMetadata("AssetType", "runtime");
            item.SetMetadata("CopyLocal", "true");
            item.SetMetadata("DestinationSubPath", Path.GetFileName(asset));
        }
        else
        {
            item.SetMetadata("HintPath", full);
            item.SetMetadata("Private", "false");
            item.SetMetadata("ExternallyResolved", "true");
            item.SetMetadata("NuGetSourceType", "Package");
        }
        return item;
    }
}
