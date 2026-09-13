using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.Composition;

namespace NetWasm.Compiler.Tasks.MsBuild;

public sealed class NetWasmBuildRawBindingsTask : Microsoft.Build.Utilities.Task
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private readonly ICompilerBuildMetadataReader _compilerMetadata;
    private readonly IHostInteropManifestReader _interopManifests;
    private readonly IRawBindingSessionFactory _sessions;
    private readonly IByteArtifactWriter _artifacts;

    public NetWasmBuildRawBindingsTask()
        : this(
            CompilerTaskComposition.CreateCompilerBuildMetadataReader(),
            CompilerTaskComposition.CreateHostInteropManifestReader(),
            CompilerTaskComposition.CreateRawBindingSessionFactory(),
            CompilerTaskComposition.CreateByteArtifactWriter())
    {
    }

    internal NetWasmBuildRawBindingsTask(
        ICompilerBuildMetadataReader compilerMetadata,
        IHostInteropManifestReader interopManifests,
        IRawBindingSessionFactory sessions,
        IByteArtifactWriter artifacts)
    {
        _compilerMetadata = compilerMetadata ??
            throw new ArgumentNullException(nameof(compilerMetadata));
        _interopManifests = interopManifests ??
            throw new ArgumentNullException(nameof(interopManifests));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    }

    [Required]
    public string CompilerMetadataPath { get; set; } = string.Empty;

    [Required]
    public string InteropManifestPath { get; set; } = string.Empty;

    [Required]
    public string WitPath { get; set; } = string.Empty;

    public string World { get; set; } = string.Empty;

    [Required]
    public string RuntimeWitPath { get; set; } = string.Empty;

    public string RuntimeWorld { get; set; } = string.Empty;

    [Required]
    public string Target { get; set; } = string.Empty;

    [Required]
    public string RuntimeModulePath { get; set; } = string.Empty;

    [Required]
    public string FinalModulePath { get; set; } = string.Empty;

    [Required]
    public string NodePath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsCommandPath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsModulePath { get; set; } = string.Empty;

    [Required]
    public string InspectionScriptPath { get; set; } = string.Empty;

    [Required]
    public string BinaryenPath { get; set; } = string.Empty;

    [Required]
    public string AdapterPath { get; set; } = string.Empty;

    [Output]
    public ITaskItem[] Adapters { get; private set; } = [];

    [Output]
    public ITaskItem[] RequiredImports { get; private set; } = [];

    [Output]
    public ITaskItem[] RequiredImportModules { get; private set; } = [];

    public override bool Execute()
    {
        Adapters = [];
        RequiredImports = [];
        RequiredImportModules = [];
        try
        {
            var target = Target switch
            {
                "wasm32" => WasmTarget.Wasm32,
                "wasm64" => WasmTarget.Wasm64,
                _ => throw new InvalidOperationException(
                    "The NetWasm raw-binding target must be wasm32 or wasm64."),
            };
            var metadata = _compilerMetadata.Read(CompilerMetadataPath);
            if (!string.Equals(metadata.Target, Target, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The NetWasm compiler metadata target does not match the raw binding target.");
            }
            var interop = _interopManifests.Read(InteropManifestPath, Target);
            using var session = _sessions.Create(
                CompilerTaskComposition.CreateWasmToolsCommand(
                    NodePath,
                    WasmToolsCommandPath,
                    WasmToolsModulePath));
            var result = session.Build(new(
                new(metadata.FunctionImports, interop),
                WitPath,
                NullIfEmpty(World),
                target,
                Inspection(RuntimeModulePath),
                Inspection(FinalModulePath))
            {
                RuntimeWitPath = RuntimeWitPath,
                RuntimeWorld = NullIfEmpty(RuntimeWorld),
            });
            _artifacts.Write(AdapterPath, result.Adapter);
            Adapters = [CreateAdapterItem()];
            RequiredImports = [.. result.RequiredImports.Select(CreateFunctionItem)];
            RequiredImportModules =
            [
                .. result.RequiredImports
                    .Select(function => function.Interface)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .Select(module => new TaskItem(module)),
            ];
            return true;
        }
        catch (CompilerException exception)
        {
            Log.LogError(exception.Diagnostic.ToString());
            return false;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK027: {exception.Message}");
            return false;
        }
    }

    private RawModuleInspectionRequest Inspection(string modulePath) => new(
        NodePath,
        InspectionScriptPath,
        Path.GetFullPath(modulePath),
        Path.GetFullPath(BinaryenPath));

    private TaskItem CreateAdapterItem()
    {
        var item = new TaskItem(Path.GetFullPath(AdapterPath));
        item.SetMetadata("Kind", "RawAdapter");
        item.SetMetadata("MediaType", "text/javascript");
        item.SetMetadata("WasmTarget", Target);
        return item;
    }

    private static TaskItem CreateFunctionItem(
        WitInterfaceFunction function)
    {
        var item = new TaskItem($"{function.Interface}/{function.Name}");
        item.SetMetadata("Interface", function.Interface);
        item.SetMetadata("Name", function.Name);
        item.SetMetadata("Parameters", JsonSerializer.Serialize(
            function.Parameters,
            MetadataJsonOptions));
        item.SetMetadata("Results", JsonSerializer.Serialize(
            function.Results,
            MetadataJsonOptions));
        return item;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
