using System.Collections.Immutable;

namespace NetWasm.Compiler.ComponentModel.Browser;

public sealed record BrowserWasmTextModule(string OutputPath, string Text);

public sealed record BrowserBinaryenInvocation(string ToolId, ImmutableArray<string> Arguments);

public sealed record BrowserComponentExportPruning(string InputPath, string OutputPath, string Prefix);

/// <summary>
/// Parse TextModules, run Merge, apply ExportPruning to the merged bytes, and run
/// Optimization in that order. Release the owned CleanupPaths on success or failure.
/// The plan records authoritative commands; it does not execute tools.
/// </summary>
public sealed record BrowserComponentCoreModuleLinkPlan(
    ImmutableArray<BrowserWasmTextModule> TextModules,
    BrowserBinaryenInvocation Merge,
    BrowserComponentExportPruning ExportPruning,
    BrowserBinaryenInvocation Optimization,
    ImmutableArray<string> CleanupPaths);
