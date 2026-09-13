using System.Collections.Immutable;
using NetWasm.Compiler.Tasks.Artifacts;

namespace NetWasm.Compiler.Tasks.MsBuild;

internal sealed class CompilerArtifactManifestTaskRequestBuilder : ICompilerArtifactManifestTaskRequestBuilder
{
    public CompilerArtifactManifestBuildRequest Build(CompilerArtifactManifestTaskInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = ImmutableArray.CreateBuilder<CompilerArtifactInputRequest>();
        inputs.Add(new("InputAssembly", input.InputAssemblyPath));
        AddInputs(inputs, "Reference", input.References);
        AddInputs(
            inputs,
            "Source",
            input.Sources.Where(source => !IsGenerated(
                source,
                input.ProjectDirectory,
                input.GeneratedSourceRoot)));
        if (!string.IsNullOrWhiteSpace(input.RuntimeAbiManifestPath))
        {
            inputs.Add(new("RuntimeAbiManifest", input.RuntimeAbiManifestPath));
        }

        var outputs = input.Artifacts
            .Select(item => new CompilerArtifactOutputRequest(
                GetMetadata(item, "Kind", "Unknown"),
                GetMetadata(item, "MediaType", "application/octet-stream"),
                item.ItemSpec))
            .ToImmutableArray();

        return new CompilerArtifactManifestBuildRequest(
            input.ManifestPath,
            input.ProjectDirectory,
            input.Profile,
            input.Target,
            input.FeatureSet,
            input.SdkVersion,
            input.CompilerVersion,
            input.RuntimeAbiVersion,
            input.RuntimeVersion,
            inputs.ToImmutable(),
            outputs);
    }

    private static void AddInputs(
        ImmutableArray<CompilerArtifactInputRequest>.Builder destination,
        string kind,
        IEnumerable<Microsoft.Build.Framework.ITaskItem> items)
    {
        foreach (var item in items)
        {
            destination.Add(new(kind, item.ItemSpec));
        }
    }

    private static bool IsGenerated(
        Microsoft.Build.Framework.ITaskItem item,
        string projectDirectory,
        string? generatedSourceRoot)
    {
        if (bool.TryParse(item.GetMetadata("AutoGen"), out var generated) && generated)
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(generatedSourceRoot))
        {
            return false;
        }

        var project = Path.GetFullPath(projectDirectory);
        var root = Path.GetFullPath(generatedSourceRoot, project);
        var source = item.GetMetadata("FullPath");
        source = string.IsNullOrWhiteSpace(source) ? item.ItemSpec : source;
        var fullSource = Path.GetFullPath(source, project);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return fullSource.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMetadata(
        Microsoft.Build.Framework.ITaskItem item,
        string name,
        string fallback)
    {
        var value = item.GetMetadata(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
