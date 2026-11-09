using NetWasm.Compiler.ComponentModel.Merging;
using System;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentCoreModuleLinkExecution
{
    void Run(
        ComponentCoreModuleLinkRequest request,
        ComponentCoreModuleWorkspace workspace);
}

public sealed class ComponentCoreModuleLinkExecution(
    IComponentCoreModuleMergeRunner merge,
    IEmscriptenEnvironmentShimWriter environmentShim,
    INetWasmHostComponentShimWriter hostShim,
    IManagedExecutableComponentAdapterWriter managedExecutables,
    IComponentCoreModuleOptimizer optimizer,
    IWasmOptimizationSettingsResolver optimizationSettings) : IComponentCoreModuleLinkExecution
{
    private readonly IComponentCoreModuleMergeRunner _merge = merge ??
        throw new ArgumentNullException(nameof(merge));
    private readonly IEmscriptenEnvironmentShimWriter _environmentShim =
        environmentShim ?? throw new ArgumentNullException(nameof(environmentShim));
    private readonly INetWasmHostComponentShimWriter _hostShim = hostShim ??
        throw new ArgumentNullException(nameof(hostShim));
    private readonly IManagedExecutableComponentAdapterWriter _managedExecutables =
        managedExecutables ?? throw new ArgumentNullException(nameof(managedExecutables));
    private readonly IComponentCoreModuleOptimizer _optimizer = optimizer ??
        throw new ArgumentNullException(nameof(optimizer));
    private readonly IWasmOptimizationSettingsResolver _optimizationSettings = optimizationSettings ??
        throw new ArgumentNullException(nameof(optimizationSettings));

    public void Run(
        ComponentCoreModuleLinkRequest request,
        ComponentCoreModuleWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        try
        {
            var settings = _optimizationSettings.Resolve(request.Optimization, request.OptimizationOverrides);
            _environmentShim.Write(workspace.EnvironmentModulePath, request.Target);
            string? hostModulePath = null;
            string? managedExecutableAdapterModulePath = null;
            if (request.ManagedExecutableEntryPoint is not null)
            {
                _hostShim.Write(new(
                    workspace.HostModulePath,
                    request.Target,
                    request.StructuredDiagnostics));
                _managedExecutables.Write(new(
                    workspace.ManagedExecutableAdapterModulePath,
                    request.Target,
                    request.ManagedExecutableEntryPoint,
                    request.StructuredDiagnostics));
                hostModulePath = workspace.HostModulePath;
                managedExecutableAdapterModulePath =
                    workspace.ManagedExecutableAdapterModulePath;
            }
            var wasmTarget = request.Target.Width == "wasm64"
                ? WasmTarget.Wasm64
                : WasmTarget.Wasm32;
            // A component exports only its canonical ABI.
            _merge.Run(new ComponentCoreModuleMergeRequest(
                request.ApplicationModulePath,
                request.RuntimeModulePath,
                workspace.EnvironmentModulePath,
                workspace.SanitizedModulePath,
                request.Target,
                hostModulePath,
                managedExecutableAdapterModulePath)
            {
                ApplicationInlineAnnotations = settings.ManagedInlineAnnotations,
                RuntimeInlineAnnotations = settings.RuntimeInlineAnnotations,
                Exports = new(CanonicalAbiNames.ModulePrefix(wasmTarget), []),
                Intermediates = new(workspace.SanitizedModulePath, workspace.SanitizedRuntimeModulePath,
                    workspace.MergedModulePath),
                KeepNames = DiagnosticNames.Kept,
            });
            _optimizer.Optimize(
                workspace.SanitizedModulePath,
                request.OutputPath,
                request.Target,
                settings.BackendPreset,
                settings.Converge,
                settings.LowMemoryUnused);
        }
        finally
        {
            workspace.Dispose();
        }
    }
}
