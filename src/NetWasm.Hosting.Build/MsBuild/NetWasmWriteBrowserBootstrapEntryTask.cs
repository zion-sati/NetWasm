using Microsoft.Build.Framework;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.MsBuild;

public sealed class NetWasmWriteBrowserBootstrapEntryTask : Microsoft.Build.Utilities.Task
{
    private readonly IBrowserBootstrapEntryWriter _writer;

    public NetWasmWriteBrowserBootstrapEntryTask()
        : this(new BrowserBootstrapEntryWriter())
    {
    }

    internal NetWasmWriteBrowserBootstrapEntryTask(IBrowserBootstrapEntryWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    [Required] public string HostingBrowserModulePath { get; set; } = string.Empty;
    [Required] public string Preview2ShimRoot { get; set; } = string.Empty;
    [Required] public string OutputPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            _writer.Write(new(
                Path.GetFullPath(HostingBrowserModulePath),
                Path.GetFullPath(Preview2ShimRoot),
                Path.GetFullPath(OutputPath)));
            return true;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK042: {exception.Message}");
            return false;
        }
    }
}
