using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.Composition;

namespace NetWasm.Compiler.Tasks.MsBuild;

public sealed class NetWasmReadCompilerBuildMetadataTask : Microsoft.Build.Utilities.Task
{
    private readonly ICompilerBuildMetadataReader _metadata;

    public NetWasmReadCompilerBuildMetadataTask()
        : this(CompilerTaskComposition.CreateCompilerBuildMetadataReader())
    {
    }

    internal NetWasmReadCompilerBuildMetadataTask(
        ICompilerBuildMetadataReader metadata)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    [Required]
    public string MetadataPath { get; set; } = string.Empty;

    [Output]
    public string Target { get; private set; } = string.Empty;

    [Output]
    public ITaskItem[] RuntimeFeatures { get; private set; } = [];

    public override bool Execute()
    {
        Target = string.Empty;
        RuntimeFeatures = [];
        try
        {
            var metadata = _metadata.Read(MetadataPath);
            Target = metadata.Target;
            RuntimeFeatures = [.. metadata.RuntimeFeatures.Select(feature => new TaskItem(feature))];
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK033: {exception.Message}");
            return false;
        }
    }
}
