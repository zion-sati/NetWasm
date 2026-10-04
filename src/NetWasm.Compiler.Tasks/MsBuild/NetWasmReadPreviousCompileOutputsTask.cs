using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.Composition;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NetWasm.Compiler.Tasks.MsBuild;

public sealed class NetWasmReadPreviousCompileOutputsTask : MsBuildTask
{
    private readonly ICompilerArtifactManifestReader _manifests;

    public NetWasmReadPreviousCompileOutputsTask()
        : this(CompilerTaskComposition.CreateArtifactManifestReader())
    {
    }

    internal NetWasmReadPreviousCompileOutputsTask(
        ICompilerArtifactManifestReader manifests)
    {
        _manifests = manifests ?? throw new ArgumentNullException(nameof(manifests));
    }

    [Required]
    public string ArtifactManifestPath { get; set; } = string.Empty;

    [Required]
    public string NativeCallbackObjectPath { get; set; } = string.Empty;

    [Output]
    public bool IsReadable { get; private set; } = true;

    [Output]
    public ITaskItem[] ConditionalOutputs { get; private set; } = [];

    public override bool Execute()
    {
        if (!File.Exists(ArtifactManifestPath))
        {
            return true;
        }

        try
        {
            var manifest = _manifests.Read(ArtifactManifestPath);
            var callbacks = manifest.Artifacts
                .Where(static artifact => artifact.Kind ==
                    CompilerArtifactKinds.NativeCallbackSupportObject)
                .ToArray();
            if (callbacks.Length == 0)
            {
                return true;
            }
            if (callbacks.Length != 1 ||
                !string.Equals(
                    Path.GetFileName(callbacks[0].Path),
                    Path.GetFileName(NativeCallbackObjectPath),
                    StringComparison.Ordinal))
            {
                IsReadable = false;
                return true;
            }

            ConditionalOutputs = [new TaskItem(NativeCallbackObjectPath)];
            return true;
        }
        catch (Exception)
        {
            IsReadable = false;
            return true;
        }
    }
}
