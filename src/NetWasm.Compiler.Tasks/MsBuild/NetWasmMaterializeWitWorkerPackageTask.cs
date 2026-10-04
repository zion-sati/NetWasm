using Microsoft.Build.Framework;
using NetWasm.Compiler.Tasks.ComponentModel;

namespace NetWasm.Compiler.Tasks.MsBuild;

public sealed class NetWasmMaterializeWitWorkerPackageTask : Microsoft.Build.Utilities.Task
{
    private readonly IWitWorkerPackageMaterializer _materializer;

    public NetWasmMaterializeWitWorkerPackageTask()
        : this(new WitWorkerPackageMaterializer())
    {
    }

    internal NetWasmMaterializeWitWorkerPackageTask(
        IWitWorkerPackageMaterializer materializer)
    {
        _materializer = materializer ??
            throw new ArgumentNullException(nameof(materializer));
    }

    [Required] public string AuthoredWitPath { get; set; } = string.Empty;
    [Required] public string PlatformWitPackagePath { get; set; } = string.Empty;
    [Required] public string OutputDirectory { get; set; } = string.Empty;
    [Output] public string ResolvedWitPath { get; private set; } = string.Empty;

    public override bool Execute()
    {
        ResolvedWitPath = string.Empty;
        try
        {
            ResolvedWitPath = _materializer.Materialize(new(
                AuthoredWitPath,
                PlatformWitPackagePath,
                OutputDirectory));
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK071: {exception.Message}");
            return false;
        }
    }
}
