using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.Tasks.Framework;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NetWasm.Compiler.Tasks.MsBuild;

// Gives the compiler the NetWasm.App assemblies the program's assemblies reach, as the SDK passes a
// framework's assemblies with an application's own, rather than all of them, and names the program
// assemblies (a package's older copy of a framework assembly) they replace.
public sealed class NetWasmSelectFrameworkAssembliesTask : MsBuildTask
{
    private readonly IFrameworkAssemblySelector _selector;

    public NetWasmSelectFrameworkAssembliesTask()
        : this(new FrameworkAssemblySelector(new AssemblyMetadataReader()))
    {
    }

    internal NetWasmSelectFrameworkAssembliesTask(IFrameworkAssemblySelector selector)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
    }

    [Required]
    public ITaskItem[] ProgramAssemblies { get; set; } = [];

    public ITaskItem[] FrameworkAssemblies { get; set; } = [];

    // The compiler's metadata facades (netstandard.dll): a reference to one reaches what it forwards to.
    public ITaskItem[] Facades { get; set; } = [];

    [Output]
    public ITaskItem[] SelectedAssemblies { get; private set; } = [];

    [Output]
    public ITaskItem[] DisplacedAssemblies { get; private set; } = [];

    public override bool Execute()
    {
        try
        {
            var framework = FrameworkAssemblies.DistinctBy(item => item.GetMetadata("FullPath"), StringComparer.Ordinal)
                .ToDictionary(item => item.GetMetadata("FullPath"), StringComparer.Ordinal);
            var program = ProgramAssemblies.DistinctBy(item => item.GetMetadata("FullPath"), StringComparer.Ordinal)
                .ToDictionary(item => item.GetMetadata("FullPath"), StringComparer.Ordinal);
            var selection = _selector.Select([.. program.Keys], [.. framework.Keys],
                [.. Facades.Select(item => item.GetMetadata("FullPath")).Distinct(StringComparer.Ordinal)]);
            SelectedAssemblies = [.. selection.Framework.Select(path => (ITaskItem)new TaskItem(framework[path]))];
            DisplacedAssemblies = [.. selection.Displaced.Select(path => (ITaskItem)new TaskItem(program[path]))];
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            BadImageFormatException or ArgumentException)
        {
            Log.LogError($"NWSDK104: Could not select the NetWasm.App assemblies the program reaches: {exception.Message}");
            return false;
        }
    }
}
