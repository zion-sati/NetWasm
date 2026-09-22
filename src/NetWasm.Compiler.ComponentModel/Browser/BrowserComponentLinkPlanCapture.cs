using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel.Browser;

internal sealed class BrowserComponentLinkPlanCapture(ImmutableHashSet<string> ownedPaths)
{
    private enum Phase { Text, Merged, Pruned, Validated, Finalized, Cleanup }

    private readonly ImmutableHashSet<string> _ownedPaths = ownedPaths;
    private readonly List<BrowserWasmTextModule> _modules = [];
    private readonly List<string> _cleanup = [];
    private Phase _phase;
    private BrowserBinaryenInvocation? _merge;
    private BrowserComponentExportPruning? _exports;
    private BrowserBinaryenInvocation? _optimization;
    private BrowserCoreModuleValidation? _validation;
    private BrowserFileCopy? _copy;

    public void AddText(string source, string outputPath)
    {
        RequirePhase(Phase.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        RequireOwnedPath(outputPath);
        if (_modules.Exists(module => string.Equals(module.OutputPath, outputPath, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("A text module was captured more than once.");
        }
        _modules.Add(new(outputPath, source));
    }

    public void AddInvocation(string toolId, ImmutableArray<string> arguments)
    {
        if (arguments.IsDefault)
        {
            throw new InvalidOperationException("A planned invocation requires explicit arguments.");
        }
        if (toolId == BinaryenToolIds.WasmMerge)
        {
            RequirePhase(Phase.Text);
            if (_modules.Count == 0)
            {
                throw new InvalidOperationException("Merge planning requires its support modules first.");
            }
            _merge = new(toolId, [.. arguments]);
            _phase = Phase.Merged;
            return;
        }
        if (toolId == BinaryenToolIds.WasmOpt)
        {
            RequirePhase(Phase.Pruned);
            _optimization = new(toolId, [.. arguments]);
            _phase = Phase.Finalized;
            return;
        }
        throw new InvalidOperationException("The link producer requested an unexpected tool.");
    }

    public void AddExportPruning(string inputPath, string outputPath, string prefix)
    {
        RequirePhase(Phase.Merged);
        RequireOwnedPath(inputPath);
        RequireOwnedPath(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _exports = new(inputPath, outputPath, prefix);
        _phase = Phase.Pruned;
    }

    public bool PlannedFileExists(string path) =>
        _phase == Phase.Pruned && string.Equals(_exports!.OutputPath, path, StringComparison.Ordinal);

    public void AddCopy(string inputPath, string outputPath)
    {
        RequirePhase(Phase.Validated);
        _copy = new(inputPath, outputPath);
        _phase = Phase.Finalized;
    }

    public void AddValidation(string path)
    {
        RequirePhase(Phase.Pruned);
        if (!string.Equals(_exports!.OutputPath, path, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Core validation requires the export-processed module.");
        }
        _validation = new(path);
        _phase = Phase.Validated;
    }

    public void AddCleanup(string path)
    {
        RequireOwnedPath(path);
        if (_cleanup.Contains(path))
        {
            throw new InvalidOperationException("An owned intermediate was released more than once.");
        }
        // Cleanup also runs when an authoritative producer fails. No snapshot is
        // returned in that case; recording cleanup must preserve the producer failure.
        _phase = Phase.Cleanup;
        _cleanup.Add(path);
    }

    public BrowserComponentCoreModuleLinkPlan Snapshot()
    {
        RequirePhase(Phase.Cleanup);
        if (_cleanup.Count != _ownedPaths.Count)
        {
            throw new InvalidOperationException("The link producer did not capture a complete plan.");
        }
        return new([.. _modules], _merge!, _exports!, _optimization, _validation, _copy,
            [.. _cleanup]);
    }

    private void RequireOwnedPath(string path)
    {
        if (!_ownedPaths.Contains(path))
        {
            throw new InvalidOperationException("The link producer requested an unowned intermediate.");
        }
    }

    private void RequirePhase(Phase expected)
    {
        if (_phase != expected)
        {
            throw new InvalidOperationException("The link producer requested an unexpected stage or repeat.");
        }
    }
}

internal sealed class BrowserWasmTextModuleCapture(BrowserComponentLinkPlanCapture capture) : IWasmTextModuleWriter
{
    public void Write(string source, string outputPath) => capture.AddText(source, outputPath);
}

internal sealed class BrowserBinaryenInvocationCapture(BrowserComponentLinkPlanCapture capture) : IBinaryenToolRunner
{
    public ToolResult Run(string toolId, ImmutableArray<string> arguments)
    {
        capture.AddInvocation(toolId, arguments);
        return new(0, string.Empty, string.Empty);
    }
}

internal sealed class BrowserComponentExportPruningCapture(BrowserComponentLinkPlanCapture capture)
    : IWasmCoreModuleExportEditor
{
    public void RetainComponentExports(string inputPath, string outputPath, string prefix) =>
        capture.AddExportPruning(inputPath, outputPath, prefix);
}

internal sealed class BrowserPlannedFileExistence(BrowserComponentLinkPlanCapture capture) : IFileExistence
{
    public bool Exists(string path) => capture.PlannedFileExists(path);
}

internal sealed class BrowserFileCopyCapture(BrowserComponentLinkPlanCapture capture) : IFileCopier
{
    public void Copy(string sourcePath, string destinationPath) =>
        capture.AddCopy(sourcePath, destinationPath);
}

internal sealed class BrowserCoreModuleValidationCapture(BrowserComponentLinkPlanCapture capture) :
    IWasmCoreModuleValidator
{
    public void Validate(string path) => capture.AddValidation(path);
}

internal sealed class BrowserCleanupPathCapture(BrowserComponentLinkPlanCapture capture) : IFileDeleter
{
    public void Delete(string path) => capture.AddCleanup(path);
}
