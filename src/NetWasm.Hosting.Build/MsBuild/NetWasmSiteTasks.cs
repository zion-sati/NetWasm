using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Hosting.Build.Execution;

namespace NetWasm.Hosting.Build.MsBuild;

/// <summary>
/// Joins a browser project's site files (NetWasmSiteFile, with SitePath and Fallback) to the files
/// it publishes (ResolvedFileToPublish): what publish copies.
/// </summary>
public sealed class NetWasmResolveSiteFilesTask : Microsoft.Build.Utilities.Task
{
    private readonly ISiteFileResolver _resolver;

    public NetWasmResolveSiteFilesTask() : this(new SiteFileResolver()) { }

    internal NetWasmResolveSiteFilesTask(ISiteFileResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public ITaskItem[] PublishFiles { get; set; } = [];
    public ITaskItem[] SiteFiles { get; set; } = [];

    [Output] public ITaskItem[] FilesToPublish { get; private set; } = [];

    public override bool Execute()
    {
        var sites = SiteFiles.Select(item => new SiteFile(item.ItemSpec, item.GetMetadata("SitePath"),
            string.Equals(item.GetMetadata("Fallback"), "true", StringComparison.OrdinalIgnoreCase))).ToArray();
        var publication = _resolver.Resolve(
            [.. PublishFiles.Select(item => new PublishedFile(item.ItemSpec, item.GetMetadata("RelativePath")))], sites);
        FilesToPublish =
        [
            .. PublishFiles.Where((_, index) => !publication.Replaced.Contains(index)),
            .. publication.Added.Select(file =>
            {
                var item = new TaskItem(SiteFiles[file.Index]);
                item.SetMetadata("RelativePath", file.SitePath);
                item.SetMetadata("CopyToPublishDirectory", "PreserveNewest");
                return (ITaskItem)item;
            }),
        ];
        return true;
    }
}

/// <summary>Writes the map dotnet run serves a page's site from: every file publish copies, at its path.</summary>
public sealed class NetWasmWriteSiteMapTask : Microsoft.Build.Utilities.Task
{
    private readonly ISiteMapWriter _writer;

    public NetWasmWriteSiteMapTask() : this(new SiteMapWriter()) { }

    internal NetWasmWriteSiteMapTask(ISiteMapWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public ITaskItem[] Files { get; set; } = [];
    [Required] public string OutputPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            _writer.Write(OutputPath, [.. Files.Select(item => new PublishedFile(item.ItemSpec, item.GetMetadata("RelativePath")))]);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.LogError($"NWSDK094: the site map could not be written: {exception.Message}");
            return false;
        }
    }
}
