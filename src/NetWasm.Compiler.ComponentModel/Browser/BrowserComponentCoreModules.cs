using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.ManagedExecutables;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel.Browser;

/// <summary>Plans component core linking and edits exports without host file or process access.</summary>
public static class BrowserComponentCoreModules
{
    public static BrowserComponentCoreModuleLinkPlan CreateLinkPlan(
        ComponentCoreModuleLinkRequest request, BrowserComponentCoreModuleWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request.Target);
        var paths = ValidatePaths(request, workspace);
        var capture = new BrowserComponentLinkPlanCapture(paths);
        var modules = new BrowserWasmTextModuleCapture(capture);
        var tools = new BrowserBinaryenInvocationCapture(capture);
        var adapters = new ManagedExecutableComponentAdapterWriter(
            new KeyValuePair<ManagedExecutableCompletionShape, IManagedExecutableComponentAdapterWriter>[]
            {
                new(ManagedExecutableCompletionShape.Synchronous,
                    new SynchronousManagedExecutableComponentAdapterWriter(modules)),
                new(ManagedExecutableCompletionShape.Asynchronous,
                    new AsynchronousManagedExecutableComponentAdapterWriter(modules)),
            });
        var execution = new ComponentCoreModuleLinkExecution(
            new ComponentCoreModuleMergeRunner(tools),
            new EmscriptenEnvironmentShimWriter(modules),
            new NetWasmHostComponentShimWriter(modules),
            adapters,
            new BrowserComponentExportPruningCapture(capture),
            new ComponentCoreModuleOptimizer(tools, new BrowserPlannedFileExistence(capture)));
        execution.Run(request, new ComponentCoreModuleWorkspace(
            new BrowserCleanupPathCapture(capture),
            workspace.EnvironmentModulePath,
            workspace.HostModulePath,
            workspace.ManagedExecutableAdapterModulePath,
            workspace.MergedModulePath,
            workspace.SanitizedModulePath));
        return capture.Snapshot();
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
            workspace.SanitizedModulePath);
        var allPaths = new[]
        {
            request.ApplicationModulePath, request.RuntimeModulePath, request.OutputPath,
            workspace.EnvironmentModulePath, workspace.HostModulePath,
            workspace.ManagedExecutableAdapterModulePath, workspace.MergedModulePath,
            workspace.SanitizedModulePath,
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
