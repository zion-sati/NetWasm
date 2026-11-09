using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Browser.Plans;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ComponentModel.ManagedExecutables;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Browser;

/// <summary>Plans component core linking and edits exports without host file or process access.</summary>
public static class BrowserComponentCoreModules
{
    public static BrowserLinkPlan CreateOrderedLinkPlan(
        ComponentCoreModuleLinkRequest request, BrowserComponentCoreModuleWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request.Target);
        var owned = ValidatePaths(request, workspace);
        ImmutableArray<string> inputs = [request.ApplicationModulePath, request.RuntimeModulePath];
        ImmutableArray<string> outputs = [request.OutputPath];
        ImmutableArray<string> temporary = [workspace.EnvironmentModulePath, workspace.HostModulePath,
            workspace.ManagedExecutableAdapterModulePath, workspace.MergedModulePath, workspace.SanitizedModulePath,
            workspace.SanitizedRuntimeModulePath];
        var state = new BrowserLinkCaptureState(inputs, outputs, temporary);
        var recorder = new BrowserLinkOperationRecorder(state, BrowserLinkPlanValidation.CreateInspector());
        var modules = new BrowserLinkTextCapture(recorder);
        var tools = BrowserLinkPlanValidation.CreateToolCapture(recorder);
        var inline = new BrowserLinkInlineCapture(recorder);
        var execution = new ComponentCoreModuleLinkExecution(
            new ComponentCoreModuleMergeRunner(tools, inline, new BrowserLinkExportCapture(recorder)),
            new EmscriptenEnvironmentShimWriter(modules),
            new PortableCommandHostComponentShimWriter(modules),
            new ManagedExecutableComponentAdapterWriter(
            [
                new(ManagedExecutableCompletionShape.Synchronous, new SynchronousManagedExecutableComponentAdapterWriter(modules)),
                new(ManagedExecutableCompletionShape.Asynchronous, new CallbackManagedExecutableComponentAdapterWriter(modules)),
            ]),
            new ComponentCoreModuleOptimizer(tools, new BrowserLinkCaptureFileExistence(state),
                new WasmCoreModuleValidator(new BrowserLinkValidationCapture(recorder)), inline),
            new WasmOptimizationSettingsResolver());
        execution.Run(request, new ComponentCoreModuleWorkspace(new BrowserLinkFileCleanupCapture(owned),
            workspace.EnvironmentModulePath, workspace.HostModulePath, workspace.ManagedExecutableAdapterModulePath,
            workspace.MergedModulePath, workspace.SanitizedModulePath, workspace.SanitizedRuntimeModulePath));
        return BrowserLinkPlanValidation.CreateBuilder().Build([.. state.Operations], inputs, outputs, temporary);
    }

    /// <summary>
    /// Applies the authoritative component export policy to caller-provided bytes.
    /// Returns an owned module and propagates CompilerException diagnostics.
    /// </summary>
    public static byte[] RetainComponentExports(ReadOnlyMemory<byte> module, string prefix) =>
        BrowserComponentExportPruner.Retain(module, prefix);

    private static ImmutableHashSet<string> ValidatePaths(
        ComponentCoreModuleLinkRequest request, BrowserComponentCoreModuleWorkspace workspace)
    {
        var ownedPaths = ImmutableHashSet.Create(StringComparer.Ordinal,
            workspace.EnvironmentModulePath, workspace.HostModulePath,
            workspace.ManagedExecutableAdapterModulePath, workspace.MergedModulePath,
            workspace.SanitizedModulePath, workspace.SanitizedRuntimeModulePath);
        var allPaths = new[]
        {
            request.ApplicationModulePath, request.RuntimeModulePath, request.OutputPath,
            workspace.EnvironmentModulePath, workspace.HostModulePath,
            workspace.ManagedExecutableAdapterModulePath, workspace.MergedModulePath,
            workspace.SanitizedModulePath, workspace.SanitizedRuntimeModulePath,
        };
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in allPaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!unique.Add(path))
            {
                throw new ArgumentException("Link inputs, output and owned intermediates require distinct virtual paths.",
                    nameof(workspace));
            }
        }
        return ownedPaths;
    }
}
