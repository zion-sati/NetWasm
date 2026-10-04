using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.Composition;

namespace NetWasm.Compiler.Tasks.MsBuild;

public sealed class NetWasmLinkRawModuleTask : Microsoft.Build.Utilities.Task
{
    private readonly IRawModuleLinkSessionFactory _sessions;
    private readonly IInternalRuntimeExportReader _exports;

    public NetWasmLinkRawModuleTask()
        : this(CompilerTaskComposition.CreateRawModuleLinkSessionFactory(), CompilerTaskComposition.CreateInternalRuntimeExportReader())
    {
    }

    internal NetWasmLinkRawModuleTask(IRawModuleLinkSessionFactory sessions, IInternalRuntimeExportReader exports)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _exports = exports ?? throw new ArgumentNullException(nameof(exports));
    }

    [Required]
    public string CoreModulePath { get; set; } = string.Empty;

    [Required]
    public string RuntimeModulePath { get; set; } = string.Empty;

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsNodePath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsCommandPath { get; set; } = string.Empty;

    [Required]
    public string WasmToolsModulePath { get; set; } = string.Empty;

    [Required]
    public string BinaryenWasmOptPath { get; set; } = string.Empty;

    [Required]
    public string BinaryenWasmMergePath { get; set; } = string.Empty;

    public string NativeBinaryenWasmOptPath { get; set; } = string.Empty;

    public string NativeBinaryenWasmMergePath { get; set; } = string.Empty;

    [Required]
    public string Target { get; set; } = string.Empty;

    public string Optimization { get; set; } = "Oz";
    public string InternalRuntimeExports { get; set; } = string.Empty;
    public string InternalApplicationExports { get; set; } = string.Empty;

    [Output]
    public ITaskItem[] Modules { get; private set; } = [];

    public override bool Execute()
    {
        Modules = [];
        try
        {
            var componentTarget = Target switch
            {
                "wasm32" => ComponentTarget.Wasm32Wasi02,
                "wasm64" => ComponentTarget.Wasm64Wasi02,
                _ => throw new InvalidOperationException(
                    "The NetWasm raw-module target must be wasm32 or wasm64."),
            };
            var optimization = ParseOptimization();
            var internalExports = _exports.Read(InternalRuntimeExports);
            var internalApplicationExports = _exports.Read(
                InternalApplicationExports);
            using var session = _sessions.Create(
                CompilerTaskComposition.CreateWasmToolsCommand(
                    WasmToolsNodePath,
                    WasmToolsCommandPath,
                    WasmToolsModulePath),
                CompilerTaskComposition.CreateBinaryenConfiguration(
                    WasmToolsNodePath,
                    BinaryenWasmOptPath,
                    BinaryenWasmMergePath,
                    NullIfEmpty(NativeBinaryenWasmOptPath),
                    NullIfEmpty(NativeBinaryenWasmMergePath)));
            session.Link(new(
                CoreModulePath,
                RuntimeModulePath,
                OutputPath,
                componentTarget,
                optimization)
            {
                InternalRuntimeExports = internalExports,
                InternalApplicationExports = internalApplicationExports,
            });
            Modules = [CreateModuleItem()];
            return true;
        }
        catch (CompilerException exception)
        {
            Log.LogError(exception.Diagnostic.ToString());
            return false;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK026: {exception.Message}");
            return false;
        }
    }

    private TaskItem CreateModuleItem()
    {
        var item = new TaskItem(Path.GetFullPath(OutputPath));
        item.SetMetadata("Kind", "RawModule");
        item.SetMetadata("MediaType", "application/wasm");
        item.SetMetadata("WasmTarget", Target);
        item.SetMetadata("WasiVersion", "0.2");
        return item;
    }

    private FinalWasmOptimization ParseOptimization() =>
        FinalWasmOptimizationParser.Parse(Optimization);

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
