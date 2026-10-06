using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.ComponentModel.Browser;

/// <summary>Plans the authoritative raw-module link pipeline without host file or process access.</summary>
public static class BrowserRawCoreModules
{
    public static byte[] RemoveExports(
        ReadOnlyMemory<byte> module,
        ImmutableArray<WasmInternalExport> exports) =>
        BrowserComponentExportPruner.Remove(module, exports);

    public static BrowserRawCoreModuleLinkPlan CreateLinkPlan(
        RawModuleLinkRequest request,
        BrowserRawCoreModuleWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request.Target);
        ValidatePaths(request, workspace);
        var capture = new BrowserRawLinkPlanCapture(request, workspace);
        var tools = new BrowserRawBinaryenCapture(capture);
        var execution = new RawModuleLinkExecution(
            new BrowserRawEnvironmentCapture(capture),
            new ComponentCoreModuleMergeRunner(tools),
            new ComponentCoreModuleOptimizer(
                tools,
                new BrowserRawFileExistence(capture),
                new WasmCoreModuleValidator(new BrowserRawValidationCapture(capture)),
                new BrowserRawFileCopyCapture(capture)),
            new BrowserRawFileMoveCapture(capture),
            new BrowserRawExportCapture(capture));
        execution.Run(request, new ComponentPackageWorkspace(
            new BrowserRawDirectoryCleanupCapture(capture),
            workspace.TemporaryDirectory,
            workspace.LinkedModulePath,
            Path.Combine(workspace.TemporaryDirectory, "unused-embedded.wasm"),
            Path.Combine(workspace.TemporaryDirectory, "unused-unstripped.wasm"),
            Path.Combine(workspace.TemporaryDirectory, "unused-component.wasm")));
        return capture.Snapshot();
    }

    private static void ValidatePaths(
        RawModuleLinkRequest request,
        BrowserRawCoreModuleWorkspace workspace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace.TemporaryDirectory);
        var paths = new[]
        {
            request.ApplicationModulePath,
            request.RuntimeModulePath,
            request.OutputPath,
            workspace.LinkedModulePath,
            Path.Combine(workspace.TemporaryDirectory, "environment.wasm"),
            Path.Combine(workspace.TemporaryDirectory, "merged.wasm"),
            Path.Combine(workspace.TemporaryDirectory, "sanitized.wasm"),
        };
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!unique.Add(path))
            {
                throw new ArgumentException(
                    "Raw link inputs, output and owned intermediates require distinct virtual paths.",
                    nameof(workspace));
            }
        }
    }
}

internal sealed class BrowserRawLinkPlanCapture(
    RawModuleLinkRequest request,
    BrowserRawCoreModuleWorkspace workspace)
{
    private enum Phase { Initial, Environment, Merged, Pruned, Optimized, Validated, Copied, Published, Cleanup }

    private readonly RawModuleLinkRequest _request = request;
    private readonly BrowserRawCoreModuleWorkspace _workspace = workspace;
    private Phase _phase;
    private BrowserWasmTextModule? _environment;
    private BrowserBinaryenInvocation? _merge;
    private BrowserRawExportPruning? _pruning;
    private BrowserBinaryenInvocation? _optimization;
    private BrowserCoreModuleValidation? _validation;
    private BrowserFileCopy? _copy;
    private BrowserFileMove? _publication;

    public void AddEnvironment(string outputPath, string text)
    {
        Require(Phase.Initial);
        RequirePath(outputPath, Path.Combine(_workspace.TemporaryDirectory, "environment.wasm"));
        _environment = new(outputPath, text);
        _phase = Phase.Environment;
    }

    public void AddInvocation(string toolId, ImmutableArray<string> arguments)
    {
        if (toolId == BinaryenToolIds.WasmMerge)
        {
            Require(Phase.Environment);
            _merge = new(toolId, [.. arguments]);
            _phase = Phase.Merged;
            return;
        }
        if (toolId == BinaryenToolIds.WasmOpt)
        {
            if (_phase is not (Phase.Merged or Phase.Pruned))
                throw new InvalidOperationException("Raw optimization was planned out of order.");
            _optimization = new(toolId, [.. arguments]);
            _phase = Phase.Optimized;
            return;
        }
        throw new InvalidOperationException("The raw link producer requested an unexpected tool.");
    }

    public void AddPruning(string inputPath, string outputPath, ImmutableArray<WasmInternalExport> removed)
    {
        Require(Phase.Merged);
        RequirePath(inputPath, Path.Combine(_workspace.TemporaryDirectory, "merged.wasm"));
        RequirePath(outputPath, Path.Combine(_workspace.TemporaryDirectory, "sanitized.wasm"));
        if (removed.IsDefaultOrEmpty)
            throw new InvalidOperationException("Raw export pruning requires explicit exports.");
        _pruning = new(inputPath, outputPath, [.. removed]);
        _phase = Phase.Pruned;
    }

    public bool Exists(string path) => _phase switch
    {
        Phase.Merged => path == Path.Combine(_workspace.TemporaryDirectory, "merged.wasm"),
        Phase.Pruned => path == Path.Combine(_workspace.TemporaryDirectory, "sanitized.wasm"),
        _ => false,
    };

    public void AddValidation(BrowserCoreModuleValidation validation)
    {
        if (_phase is not (Phase.Merged or Phase.Pruned or Phase.Optimized))
            throw new InvalidOperationException("Raw validation was planned out of order.");
        _validation = validation;
        _phase = Phase.Validated;
    }

    public void AddCopy(string inputPath, string outputPath)
    {
        Require(Phase.Validated);
        RequirePath(outputPath, _workspace.LinkedModulePath);
        _copy = new(inputPath, outputPath);
        _phase = Phase.Copied;
    }

    public void AddPublication(string inputPath, string outputPath)
    {
        if (_phase != Phase.Copied && !(_phase == Phase.Validated && _optimization is not null))
            throw new InvalidOperationException("Raw publication was planned out of order.");
        RequirePath(inputPath, _workspace.LinkedModulePath);
        RequirePath(outputPath, _request.OutputPath);
        _publication = new(inputPath, outputPath);
        _phase = Phase.Published;
    }

    public void AddCleanup(string directory)
    {
        Require(Phase.Published);
        RequirePath(directory, _workspace.TemporaryDirectory);
        _phase = Phase.Cleanup;
    }

    public BrowserRawCoreModuleLinkPlan Snapshot()
    {
        Require(Phase.Cleanup);
        return new([_environment!], _merge!, _pruning, _optimization,
            _validation!, _copy, _publication!, _workspace.TemporaryDirectory);
    }

    private void Require(Phase phase)
    {
        if (_phase != phase)
            throw new InvalidOperationException("The raw link producer requested an unexpected stage.");
    }

    private static void RequirePath(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("The raw link producer changed an owned virtual path.");
    }
}

internal sealed class BrowserRawEnvironmentCapture(BrowserRawLinkPlanCapture capture) : IEmscriptenEnvironmentShimWriter
{
    private readonly EmscriptenEnvironmentShimWriter _writer = new(new Capture(capture));
    public void Write(string outputPath, ComponentTarget target) => _writer.Write(outputPath, target);

    private sealed class Capture(BrowserRawLinkPlanCapture capture) : IWasmTextModuleWriter
    {
        public void Write(string source, string outputPath) => capture.AddEnvironment(outputPath, source);
    }
}

internal sealed class BrowserRawBinaryenCapture(BrowserRawLinkPlanCapture capture) : IBinaryenToolRunner
{
    public ToolResult Run(string toolId, ImmutableArray<string> arguments)
    {
        capture.AddInvocation(toolId, arguments);
        return new(0, string.Empty, string.Empty);
    }
}

internal sealed class BrowserRawExportCapture(BrowserRawLinkPlanCapture capture) : IWasmCoreModuleExportEditor
{
    public void Rewrite(string inputPath, string outputPath, WasmExportSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.ComponentPrefix is not null || selection.RemovedExports.IsDefaultOrEmpty)
            throw new InvalidOperationException("The raw link plan requires explicit export removal.");
        capture.AddPruning(inputPath, outputPath, selection.RemovedExports);
    }
}

internal sealed class BrowserRawFileExistence(BrowserRawLinkPlanCapture capture) : IFileExistence
{
    public bool Exists(string path) => capture.Exists(path);
}

internal sealed class BrowserRawValidationCapture(BrowserRawLinkPlanCapture capture) : IComponentPackageOperationRunner
{
    public void Run(IEnumerable<string> arguments, string operation)
    {
        var command = arguments.ToImmutableArray();
        if (operation != "validate the linked core module" || command.Length != 4 ||
            command[0] != "validate" || command[2] != "--features")
            throw new InvalidOperationException("The raw link producer requested invalid validation.");
        capture.AddValidation(new(command[1], command));
    }
}

internal sealed class BrowserRawFileCopyCapture(BrowserRawLinkPlanCapture capture) : IFileCopier
{
    public void Copy(string sourcePath, string destinationPath) => capture.AddCopy(sourcePath, destinationPath);
}

internal sealed class BrowserRawFileMoveCapture(BrowserRawLinkPlanCapture capture) : IFileMover
{
    public void Move(string sourcePath, string destinationPath) => capture.AddPublication(sourcePath, destinationPath);
}

internal sealed class BrowserRawDirectoryCleanupCapture(BrowserRawLinkPlanCapture capture) : IDirectoryDeleter
{
    public void Delete(string path) => capture.AddCleanup(path);
}
