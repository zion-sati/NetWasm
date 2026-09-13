using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace NetWasm.Sdk.Pack.MsBuild;

/// <summary>
/// Evaluates a referenced project with its selected TFM and reads the SDK's
/// computed pack identity. It deliberately does not infer an ID from a file
/// name or create a private NuGet load context.
/// </summary>
public sealed class MsBuildProjectPackageIdentityResolver : IProjectPackageIdentityResolver
{
    public ProjectPackageIdentity? Resolve(string projectPath, ProjectEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(request.TargetFramework))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(projectPath);
            if (!File.Exists(fullPath))
            {
                return null;
            }

            using var collection = new ProjectCollection();
            var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TargetFramework"] = request.TargetFramework
            };
            AddGlobal(globals, "Configuration", request.Configuration);
            AddGlobal(globals, "Platform", request.Platform);
            AddGlobal(globals, "RuntimeIdentifier", request.RuntimeIdentifier);
            var project = collection.LoadProject(fullPath, globals, null);
            var packageId = project.GetPropertyValue("PackageId");
            var packageVersion = project.GetPropertyValue("PackageVersion");
            var isPackable = !bool.TryParse(project.GetPropertyValue("IsPackable"), out var packable) || packable;
            return string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(packageVersion)
                ? null
                : new ProjectPackageIdentity(packageId, packageVersion, isPackable);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or InvalidProjectFileException)
        {
            throw new NetWasmPackException(NetWasmPackErrorCode.NWPK015, "A referenced project could not be evaluated for packing.");
        }
    }

    private static void AddGlobal(Dictionary<string, string> globals, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            globals[name] = value;
        }
    }
}
