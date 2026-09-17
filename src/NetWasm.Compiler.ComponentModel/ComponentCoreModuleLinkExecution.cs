using System;
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
    IWasmCoreModuleExportEditor exports,
    IComponentCoreModuleOptimizer optimizer) : IComponentCoreModuleLinkExecution
{
    private readonly IComponentCoreModuleMergeRunner _merge = merge ??
        throw new ArgumentNullException(nameof(merge));
    private readonly IEmscriptenEnvironmentShimWriter _environmentShim =
        environmentShim ?? throw new ArgumentNullException(nameof(environmentShim));
    private readonly INetWasmHostComponentShimWriter _hostShim = hostShim ??
        throw new ArgumentNullException(nameof(hostShim));
    private readonly IManagedExecutableComponentAdapterWriter _managedExecutables =
        managedExecutables ?? throw new ArgumentNullException(nameof(managedExecutables));
    private readonly IWasmCoreModuleExportEditor _exports = exports ??
        throw new ArgumentNullException(nameof(exports));
    private readonly IComponentCoreModuleOptimizer _optimizer = optimizer ??
        throw new ArgumentNullException(nameof(optimizer));

    public void Run(
        ComponentCoreModuleLinkRequest request,
        ComponentCoreModuleWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        try
        {
            _environmentShim.Write(workspace.EnvironmentModulePath, request.Target);
            string? hostModulePath = null;
            string? managedExecutableAdapterModulePath = null;
            if (request.ManagedExecutableEntryPoint is not null)
            {
                _hostShim.Write(new(
                    workspace.HostModulePath,
                    request.Target));
                _managedExecutables.Write(new(
                    workspace.ManagedExecutableAdapterModulePath,
                    request.Target,
                    request.ManagedExecutableEntryPoint));
                hostModulePath = workspace.HostModulePath;
                managedExecutableAdapterModulePath =
                    workspace.ManagedExecutableAdapterModulePath;
            }
            _merge.Run(new ComponentCoreModuleMergeRequest(
                request.ApplicationModulePath,
                request.RuntimeModulePath,
                workspace.EnvironmentModulePath,
                workspace.MergedModulePath,
                request.Target,
                hostModulePath,
                managedExecutableAdapterModulePath));
            var wasmTarget = request.Target.Width == "wasm64"
                ? WasmTarget.Wasm64
                : WasmTarget.Wasm32;
            _exports.RetainComponentExports(
                workspace.MergedModulePath,
                workspace.SanitizedModulePath,
                CanonicalAbiNames.ModulePrefix(wasmTarget));
            _optimizer.Optimize(
                workspace.SanitizedModulePath,
                request.OutputPath,
                request.Target,
                request.Optimization);
        }
        finally
        {
            workspace.Dispose();
        }
    }
}
