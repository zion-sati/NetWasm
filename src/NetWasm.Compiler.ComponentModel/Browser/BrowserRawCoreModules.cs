using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Browser.Plans;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.ComponentModel.Browser;

/// <summary>Plans the authoritative raw-module link pipeline without host file or process access.</summary>
public static class BrowserRawCoreModules
{
    public static BrowserLinkPlan CreateOrderedLinkPlan(RawModuleLinkRequest request, BrowserRawCoreModuleWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request.Target);
        ValidatePaths(request, workspace);
        ImmutableArray<string> inputs = [request.ApplicationModulePath, request.RuntimeModulePath];
        ImmutableArray<string> outputs = [request.OutputPath];
        ImmutableArray<string> temporary = [workspace.LinkedModulePath, Path.Combine(workspace.TemporaryDirectory, "environment.wasm"),
                Path.Combine(workspace.TemporaryDirectory, "merged.wasm"),
                Path.Combine(workspace.TemporaryDirectory, "sanitized.wasm"),
                Path.Combine(workspace.TemporaryDirectory, "runtime-sanitized.wasm")];
        var state = new BrowserLinkCaptureState(inputs, outputs, temporary);
        var recorder = new BrowserLinkOperationRecorder(state, BrowserLinkPlanValidation.CreateInspector());
        var tools = BrowserLinkPlanValidation.CreateToolCapture(recorder);
        var inline = new BrowserLinkInlineCapture(recorder);
        var execution = new RawModuleLinkExecution(new EmscriptenEnvironmentShimWriter(new BrowserLinkTextCapture(recorder)),
            new ComponentCoreModuleMergeRunner(tools, inline, new BrowserLinkExportCapture(recorder)),
            new ComponentCoreModuleOptimizer(tools, new BrowserLinkCaptureFileExistence(state),
                new WasmCoreModuleValidator(new BrowserLinkValidationCapture(recorder)), inline),
            new BrowserLinkMoveCapture(recorder), new WasmOptimizationSettingsResolver());
        execution.Run(request, new ComponentPackageWorkspace(new BrowserLinkDirectoryCleanupCapture(workspace.TemporaryDirectory),
            workspace.TemporaryDirectory, workspace.LinkedModulePath,
            Path.Combine(workspace.TemporaryDirectory, "unused-embedded.wasm"),
            Path.Combine(workspace.TemporaryDirectory, "unused-unstripped.wasm"),
            Path.Combine(workspace.TemporaryDirectory, "unused-component.wasm")));
        return BrowserLinkPlanValidation.CreateBuilder().Build([.. state.Operations], inputs, outputs, temporary);
    }

    public static byte[] RemoveExports(
        ReadOnlyMemory<byte> module,
        ImmutableArray<WasmInternalExport> exports) =>
        BrowserComponentExportPruner.Remove(module, exports);

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
            Path.Combine(workspace.TemporaryDirectory, "runtime-sanitized.wasm"),
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
