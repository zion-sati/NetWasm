using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.ComponentModel;
using NetWasm.Compiler.Tasks.Composition;

namespace NetWasm.Compiler.Tasks.MsBuild;

public sealed class NetWasmProjectWitFunctionsTask : Microsoft.Build.Utilities.Task
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private readonly IWitFunctionProjectionSessionFactory _sessions;

    public NetWasmProjectWitFunctionsTask()
        : this(CompilerTaskComposition.CreateWitFunctionProjectionSessionFactory())
    {
    }

    internal NetWasmProjectWitFunctionsTask(
        IWitFunctionProjectionSessionFactory sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    [Required] public string WasmToolsNodePath { get; set; } = string.Empty;
    [Required] public string WasmToolsCommandPath { get; set; } = string.Empty;
    [Required] public string WasmToolsModulePath { get; set; } = string.Empty;
    [Required] public string WitPath { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;

    [Output] public ITaskItem[] RequiredImports { get; private set; } = [];
    [Output] public ITaskItem[] RequiredImportModules { get; private set; } = [];
    [Output] public ITaskItem[] Exports { get; private set; } = [];

    public override bool Execute()
    {
        RequiredImports = [];
        RequiredImportModules = [];
        Exports = [];
        try
        {
            using var session = _sessions.Create(
                CompilerTaskComposition.CreateWasmToolsCommand(
                WasmToolsNodePath,
                WasmToolsCommandPath,
                WasmToolsModulePath));
            var result = session.Project(WitPath, NullIfEmpty(World));
            RequiredImports = [.. result.Imports.Select(CreateFunctionItem)];
            RequiredImportModules = [.. result.ImportModules.Select(module =>
                new TaskItem(module))];
            Exports = [.. result.Exports.Select(CreateFunctionItem)];
            return true;
        }
        catch (CompilerException exception)
        {
            Log.LogError(exception.Diagnostic.ToString());
            return false;
        }
        catch (Exception exception)
        {
            Log.LogError($"NWSDK031: {exception.Message}");
            return false;
        }
    }

    private static TaskItem CreateFunctionItem(WitInterfaceFunction function)
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
