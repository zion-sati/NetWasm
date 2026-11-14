using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using System.Security.Cryptography;
using System.Text;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NetWasm.Sdk.Pack.References;

/// <summary>Materializes compile-only views while retaining each original input identity.</summary>
public sealed class GenerateFrameworkReferenceViewsTask : MsBuildTask
{
    private readonly IFrameworkReferenceTypeSelector selector;
    private readonly IFrameworkReferenceViewBuilder builder;

    public GenerateFrameworkReferenceViewsTask() : this(
        new CompatibleFrameworkReferenceTypeSelector(new FrameworkReferenceTypeSelector(),
            new FrameworkTypeContractChecker(new FrameworkContractTypeComparer(),
                new FrameworkTypeHierarchyReader(new FrameworkReferenceTypeRewriter(), new FrameworkContractTypeComparer()))),
        new FrameworkReferenceViewBuilder(new FrameworkReferenceTypeRewriter(),
            new FrameworkTypeContractChecker(new FrameworkContractTypeComparer(),
                new FrameworkTypeHierarchyReader(new FrameworkReferenceTypeRewriter(), new FrameworkContractTypeComparer()))))
    { }

    public GenerateFrameworkReferenceViewsTask(IFrameworkReferenceTypeSelector selector, IFrameworkReferenceViewBuilder builder)
    {
        this.selector = selector ?? throw new ArgumentNullException(nameof(selector));
        this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    [Required] public ITaskItem[] References { get; set; } = [];
    [Required] public string PlatformPath { get; set; } = "";
    [Required] public string OutputDirectory { get; set; } = "";
    [Output] public ITaskItem[] Views { get; private set; } = [];

    public override bool Execute()
    {
        Views = [];
        try
        {
            var platformBytes = File.ReadAllBytes(PlatformPath);
            using var platform = AssemblyDefinition.ReadAssembly(new MemoryStream(platformBytes));
            var views = new List<ITaskItem>();
            foreach (var reference in References)
            {
                var packageId = reference.GetMetadata("NuGetPackageId");
                if (string.IsNullOrEmpty(packageId)) continue;
                var inputPath = Path.GetFullPath(reference.ItemSpec);
                var bytes = File.ReadAllBytes(inputPath);
                using var implementation = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes));
                var names = selector.SelectTypes(new(packageId, implementation, platform));
                if (names.Count == 0) continue;
                var image = builder.Build(new(bytes, platformBytes, names));
                // Stable per input, independent of its changing contents. Recompute
                // on each invocation; byte equality preserves incremental timestamps.
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputPath)));
                var output = Path.GetFullPath(Path.Combine(OutputDirectory, key, Path.GetFileName(inputPath)));
                if (output == inputPath)
                    throw new ArgumentException("A reference view must not overwrite its implementation.");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                if (!File.Exists(output) || !File.ReadAllBytes(output).AsSpan().SequenceEqual(image))
                    File.WriteAllBytes(output, image);
                var item = new TaskItem(reference) { ItemSpec = output };
                item.SetMetadata("NetWasmOriginalReference", inputPath);
                item.SetMetadata("NetWasmMetadataReference", "true");
                views.Add(item);
            }
            Views = views.ToArray();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or BadImageFormatException or InvalidOperationException)
        {
            Log.LogError(null, "NWRF008", null, null, 0, 0, 0, 0,
                "Could not generate framework reference views: {0}", error.Message);
            return false;
        }
    }
}
