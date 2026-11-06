using NetWasm.Compiler.ComponentModel.Merging;
using System;
using System.IO;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawModuleLinkExecution
{
    void Run(RawModuleLinkRequest request, ComponentPackageWorkspace workspace);
}

public sealed class RawModuleLinkExecution(
    IEmscriptenEnvironmentShimWriter environment,
    IComponentCoreModuleMergeRunner merge,
    IComponentCoreModuleOptimizer optimizer,
    IFileMover files,
    IWasmOptimizationSettingsResolver optimizationSettings) : IRawModuleLinkExecution
{
    private readonly IEmscriptenEnvironmentShimWriter _environment = environment ??
        throw new ArgumentNullException(nameof(environment));
    private readonly IComponentCoreModuleMergeRunner _merge = merge ??
        throw new ArgumentNullException(nameof(merge));
    private readonly IComponentCoreModuleOptimizer _optimizer = optimizer ??
        throw new ArgumentNullException(nameof(optimizer));
    private readonly IFileMover _files = files ??
        throw new ArgumentNullException(nameof(files));
    private readonly IWasmOptimizationSettingsResolver _optimizationSettings = optimizationSettings ??
        throw new ArgumentNullException(nameof(optimizationSettings));

    public void Run(RawModuleLinkRequest request, ComponentPackageWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        if (request.InternalRuntimeExports.IsDefault ||
            request.InternalApplicationExports.IsDefault)
            throw ComponentException.Invalid("raw module internal exports are uninitialized");
        try
        {
            var settings = _optimizationSettings.Resolve(request.Optimization, request.OptimizationOverrides);
            var environment = Path.Combine(workspace.TemporaryDirectory, "environment.wasm");
            var merged = Path.Combine(workspace.TemporaryDirectory, "merged.wasm");
            var sanitized = Path.Combine(workspace.TemporaryDirectory, "sanitized.wasm");
            var sanitizedRuntime = Path.Combine(
                workspace.TemporaryDirectory,
                "runtime-sanitized.wasm");
            _environment.Write(environment, request.Target);
            // The internal exports only link the runtime and the application, so the merged
            // module drops them, and the optimizer can remove what only they reached.
            var removedExports = request.InternalRuntimeExports
                .AddRange(request.InternalApplicationExports);
            var optimizerInput = removedExports.IsEmpty ? merged : sanitized;
            _merge.Run(new ComponentCoreModuleMergeRequest(
                request.ApplicationModulePath,
                request.RuntimeModulePath,
                environment,
                optimizerInput,
                request.Target)
            {
                ApplicationInlineAnnotations = settings.ManagedInlineAnnotations,
                RuntimeInlineAnnotations = settings.RuntimeInlineAnnotations,
                Exports = removedExports.IsEmpty ? null : new(null, removedExports),
                Intermediates = new(sanitized, sanitizedRuntime, merged),
                KeepNames = DiagnosticNames.Kept,
            });
            _optimizer.Optimize(
                optimizerInput,
                workspace.LinkedModulePath,
                request.Target,
                settings.BackendPreset,
                settings.Converge);
            _files.Move(workspace.LinkedModulePath, request.OutputPath);
        }
        finally
        {
            workspace.Dispose();
        }
    }
}
