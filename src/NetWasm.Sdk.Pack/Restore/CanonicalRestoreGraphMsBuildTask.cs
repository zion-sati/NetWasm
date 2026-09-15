using NetWasm.Sdk.Pack.Archives;

using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NetWasm.Sdk.Pack.Restore;

public sealed class CanonicalRestoreGraphMsBuildTask : MsBuildTask
{
    private readonly IRestoreGraphCanonicalizer canonicalizer;
    private readonly IInputFileStreamOpener inputOpener;
    private readonly IOutputFileStreamOpener outputOpener;

    public CanonicalRestoreGraphMsBuildTask()
        : this(new RestoreGraphCanonicalizer(), new LocalInputFileStreamOpener(), new LocalOutputFileStreamOpener())
    {
    }

    public CanonicalRestoreGraphMsBuildTask(
        IRestoreGraphCanonicalizer canonicalizer,
        IInputFileStreamOpener inputOpener,
        IOutputFileStreamOpener outputOpener)
    {
        this.canonicalizer = canonicalizer ?? throw new ArgumentNullException(nameof(canonicalizer));
        this.inputOpener = inputOpener ?? throw new ArgumentNullException(nameof(inputOpener));
        this.outputOpener = outputOpener ?? throw new ArgumentNullException(nameof(outputOpener));
    }

    public string GraphPath { get; set; } = string.Empty;
    public string TargetFrameworkAlias { get; set; } = string.Empty;
    public string CanonicalTargetFramework { get; set; } = string.Empty;

    public override bool Execute()
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(GraphPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(TargetFrameworkAlias);
            ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalTargetFramework);
            byte[] graph;
            using (var input = inputOpener.Open(GraphPath))
            using (var memory = new MemoryStream())
            {
                input.CopyTo(memory);
                graph = memory.ToArray();
            }

            var canonical = canonicalizer.Canonicalize(graph, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TargetFrameworkAlias] = CanonicalTargetFramework
            });
            // Do not open/truncate the graph until its whole contract is valid.
            using var output = outputOpener.Open(GraphPath);
            output.Write(canonical);
            return true;
        }
        catch (Exception)
        {
            Log.LogError("NWSDK004: The generated NuGet restore graph could not preserve its canonical framework identity.");
            return false;
        }
    }
}
