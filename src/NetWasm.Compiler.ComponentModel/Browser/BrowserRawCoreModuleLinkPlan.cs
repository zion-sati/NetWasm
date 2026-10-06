using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Browser;

public sealed record BrowserRawCoreModuleWorkspace(
    string TemporaryDirectory,
    string LinkedModulePath);

public sealed record BrowserRawExportPruning(
    string InputPath,
    string OutputPath,
    ImmutableArray<WasmInternalExport> RemovedExports);

public sealed record BrowserFileMove(string InputPath, string OutputPath);

/// <summary>
/// Authoritative raw-module operations for a host that owns virtual files and tool execution.
/// </summary>
public sealed record BrowserRawCoreModuleLinkPlan(
    ImmutableArray<BrowserWasmTextModule> TextModules,
    BrowserBinaryenInvocation Merge,
    BrowserRawExportPruning? ExportPruning,
    BrowserBinaryenInvocation? Optimization,
    BrowserCoreModuleValidation Validation,
    BrowserFileCopy? Copy,
    BrowserFileMove Publication,
    string CleanupDirectory);
