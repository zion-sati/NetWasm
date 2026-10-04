using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel.Browser;

internal sealed class BrowserComponentLinkPlanCapture(ImmutableHashSet<string> ownedPaths)
{
    private enum Phase { Text, Merged, Pruned, Optimized, Validated, Finalized, Cleanup }

    private readonly ImmutableHashSet<string> _ownedPaths = ownedPaths;
    private readonly List<BrowserWasmTextModule> _modules = [];
    private readonly List<string> _cleanup = [];
    private Phase _phase;
    private BrowserBinaryenInvocation? _merge;
    private BrowserComponentExportPruning? _exports;
    private BrowserBinaryenInvocation? _optimization;
    private BrowserCoreModuleValidation? _validation;
    private BrowserFileCopy? _copy;
    private bool _finalized;

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
            _phase = Phase.Optimized;
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
        _finalized = true;
    }

    public void AddValidation(BrowserCoreModuleValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        var optimized = _phase == Phase.Optimized;
        if (!optimized) RequirePhase(Phase.Pruned);
        var expected = optimized ? _optimization!.Arguments[^1] : _exports!.OutputPath;
        if (!string.Equals(expected, validation.Path, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Core validation requires the selected final core module.");
        }
        _validation = validation;
        _phase = optimized ? Phase.Finalized : Phase.Validated;
        _finalized = optimized;
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
        if (_cleanup.Count != _ownedPaths.Count || !_finalized)
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
    public void Rewrite(string inputPath, string outputPath, WasmExportSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.RemovedExports.IsDefault || selection.ComponentPrefix is null || !selection.RemovedExports.IsEmpty)
            throw new InvalidOperationException("The browser component plan requires component-prefix export retention.");
        capture.AddExportPruning(inputPath, outputPath, selection.ComponentPrefix);
    }
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

internal sealed class BrowserCoreModuleValidationOperationCapture(BrowserComponentLinkPlanCapture capture) :
    IComponentPackageOperationRunner
{
    public void Run(IEnumerable<string> arguments, string operation)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        var command = arguments.ToImmutableArray();
        if (command.Length != 4 || command[0] != "validate" || command[2] != "--features")
        {
            throw new InvalidOperationException("The core validation capture requires an explicit validation command.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(command[3]);
        capture.AddValidation(new(command[1], command));
    }
}

internal sealed class BrowserCleanupPathCapture(BrowserComponentLinkPlanCapture capture) : IFileDeleter
{
    public void Delete(string path) => capture.AddCleanup(path);
}
