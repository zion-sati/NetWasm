using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

using NetWasm.Sdk.Pack.Archives;

using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NetWasm.Sdk.Pack.Provenance;

public sealed class SdkProvenanceManifestMsBuildTask : MsBuildTask
{
    private readonly ISdkProvenanceManifestBuilder manifestBuilder;

    public SdkProvenanceManifestMsBuildTask()
        : this(new SdkProvenanceManifestBuilder(new LocalPackageFileReader(new LocalInputFileStreamOpener())))
    {
    }

    public SdkProvenanceManifestMsBuildTask(ISdkProvenanceManifestBuilder manifestBuilder)
    {
        this.manifestBuilder = manifestBuilder ?? throw new ArgumentNullException(nameof(manifestBuilder));
    }

    public string PackageId { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public string SourceRevision { get; set; } = string.Empty;
    public string ManifestPackagePath { get; set; } = string.Empty;
    public string ManifestOutputPath { get; set; } = string.Empty;
    public ITaskItem[] PackageFiles { get; set; } = [];
    public ITaskItem[] SourceFiles { get; set; } = [];

    public override bool Execute()
    {
        try
        {
            var bytes = manifestBuilder.Build(new SdkProvenanceManifestInput(
                PackageId,
                PackageVersion,
                SourceRevision,
                ManifestPackagePath,
                PackageFiles.Select(Adapt).ToArray(),
                SourceFiles.Select(Adapt).ToArray()));
            var directory = Path.GetDirectoryName(ManifestOutputPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new NetWasmPackException(NetWasmPackErrorCode.NWPK014, "The SDK provenance manifest output path is invalid.");
            }

            Directory.CreateDirectory(directory);
            File.WriteAllBytes(ManifestOutputPath, bytes);
            Log.LogMessage(MessageImportance.Low, "Wrote SDK source provenance manifest.");
            return true;
        }
        catch (Exception exception)
        {
            var code = exception is NetWasmPackException packException ? packException.Code : NetWasmPackErrorCode.NWPK014;
            var message = exception is NetWasmPackException safeException ? safeException.SafeMessage : "The SDK provenance manifest could not be written.";
            Log.LogError($"{code}: {message}");
            return false;
        }
    }

    private static SdkProvenanceFileInput Adapt(ITaskItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var source = item.GetMetadata("SourcePath");
        var package = item.GetMetadata("PackagePath");
        var logical = item.GetMetadata("LogicalSourcePath");
        return new SdkProvenanceFileInput(source, package, string.IsNullOrWhiteSpace(logical) ? null : logical);
    }
}
