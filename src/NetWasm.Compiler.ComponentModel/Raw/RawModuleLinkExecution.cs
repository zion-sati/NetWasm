using System;
using System.IO;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawModuleLinkExecution
{
    void Run(RawModuleLinkRequest request, ComponentPackageWorkspace workspace);
}

public sealed class RawModuleLinkExecution(
    IEmscriptenEnvironmentShimWriter environment,
    IComponentCoreModuleMergeRunner merge,
    IComponentCoreModuleOptimizer optimizer,
    IFileMover files) : IRawModuleLinkExecution
{
    private readonly IEmscriptenEnvironmentShimWriter _environment = environment ??
        throw new ArgumentNullException(nameof(environment));
    private readonly IComponentCoreModuleMergeRunner _merge = merge ??
        throw new ArgumentNullException(nameof(merge));
    private readonly IComponentCoreModuleOptimizer _optimizer = optimizer ??
        throw new ArgumentNullException(nameof(optimizer));
    private readonly IFileMover _files = files ??
        throw new ArgumentNullException(nameof(files));

    public void Run(RawModuleLinkRequest request, ComponentPackageWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        try
        {
            var environment = Path.Combine(workspace.TemporaryDirectory, "environment.wasm");
            var merged = Path.Combine(workspace.TemporaryDirectory, "merged.wasm");
            _environment.Write(environment, request.Target);
            _merge.Run(new ComponentCoreModuleMergeRequest(
                request.ApplicationModulePath,
                request.RuntimeModulePath,
                environment,
                merged,
                request.Target));
            _optimizer.Optimize(
                merged,
                workspace.LinkedModulePath,
                request.Target,
                request.Optimization);
            _files.Move(workspace.LinkedModulePath, request.OutputPath);
        }
        finally
        {
            workspace.Dispose();
        }
    }
}
