using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NetWasm.Sdk.Pack.References;

/// <summary>
/// Generates NetWasm.App.Ref's framework facades: for each assembly of a .NET reference pack that NetWasm.App does
/// not ship, a forwarding facade with its identity, as the shared framework's own facades are. Packages built against
/// System.Runtime, System.Buffers or System.Threading.Tasks.Extensions then bind to NetWasm's owners. A contract whose
/// types the owners all define gets a facade; one they define only in part gets one only when it is listed as a legacy
/// contract (System.Runtime, mscorlib), whose missing types no .NET Standard library can name. A partial facade for an
/// assembly that also ships as a package (System.Drawing, WindowsBase) would displace that package's implementation.
/// </summary>
public sealed class GenerateFrameworkFacadesTask : MsBuildTask
{
    private readonly IStandardFacadeBuilder builder;

    public GenerateFrameworkFacadesTask() : this(new StandardFacadeBuilder()) { }

    public GenerateFrameworkFacadesTask(IStandardFacadeBuilder builder) =>
        this.builder = builder ?? throw new ArgumentNullException(nameof(builder));

    /// <summary>The reference pack's assemblies, such as Microsoft.NETCore.App.Ref's ref/net11.0/*.dll.</summary>
    [Required] public ITaskItem[] Contracts { get; set; } = [];

    /// <summary>The owners: CoreLib's and every NetWasm.App library's reference assembly.</summary>
    [Required] public ITaskItem[] Owners { get; set; } = [];

    /// <summary>Contract assembly names that get a facade although the owners define only part of them.</summary>
    public ITaskItem[] LegacyContracts { get; set; } = [];

    [Required] public string OutputDirectory { get; set; } = "";

    [Output] public ITaskItem[] Facades { get; private set; } = [];

    public override bool Execute()
    {
        Facades = [];
        try
        {
            var owners = Owners.Select(item => File.ReadAllBytes(item.ItemSpec)).ToArray();
            var shipped = Owners.Select(item => Path.GetFileNameWithoutExtension(item.ItemSpec))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var legacy = LegacyContracts.Select(item => item.ItemSpec).ToHashSet(StringComparer.Ordinal);
            var output = Path.GetFullPath(OutputDirectory);
            Directory.CreateDirectory(output);
            var facades = new List<ITaskItem>();
            foreach (var contract in Contracts.OrderBy(item => item.ItemSpec, StringComparer.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(contract.ItemSpec);
                if (name == "netstandard" || shipped.Contains(name)) continue;
                var facade = builder.BuildFacade(File.ReadAllBytes(contract.ItemSpec), owners);
                if (facade.Forwarded == 0 ||
                    facade.Forwarded < facade.ContractTypes && !legacy.Contains(facade.Name)) continue;
                var path = Path.Combine(output, facade.Name + ".dll");
                if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(facade.Image))
                    File.WriteAllBytes(path, facade.Image);
                var item = new TaskItem(path);
                item.SetMetadata("ContractTypes", facade.ContractTypes.ToString(System.Globalization.CultureInfo.InvariantCulture));
                item.SetMetadata("Forwarded", facade.Forwarded.ToString(System.Globalization.CultureInfo.InvariantCulture));
                facades.Add(item);
            }
            Facades = [.. facades];
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException or BadImageFormatException or InvalidOperationException)
        {
            Log.LogError(null, "NWRF007", null, null, 0, 0, 0, 0,
                "Could not generate the framework reference facades: {0}", error.Message);
            return false;
        }
    }
}
