using Microsoft.Build.Framework;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NetWasm.Sdk.Pack.References;

/// <summary>MSBuild adapter for producing the owned reference facade at package time.</summary>
public sealed class GenerateStandardFacadeTask : MsBuildTask
{
    private readonly IStandardFacadeBuilder builder;

    public GenerateStandardFacadeTask() : this(new StandardFacadeBuilder()) { }

    public GenerateStandardFacadeTask(IStandardFacadeBuilder builder) =>
        this.builder = builder ?? throw new ArgumentNullException(nameof(builder));

    [Required] public string ContractPath { get; set; } = "";
    [Required] public ITaskItem[] Owners { get; set; } = [];
    [Required] public string OutputPath { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            var image = builder.Build(File.ReadAllBytes(ContractPath),
                Owners.Select(item => File.ReadAllBytes(item.ItemSpec)).ToArray());
            var output = Path.GetFullPath(OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (!File.Exists(output) || !File.ReadAllBytes(output).AsSpan().SequenceEqual(image))
                File.WriteAllBytes(output, image);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or BadImageFormatException or InvalidOperationException)
        {
            Log.LogError(null, "NWRF007", null, null, 0, 0, 0, 0,
                "Could not generate the .NET Standard reference facade: {0}", error.Message);
            return false;
        }
    }
}
