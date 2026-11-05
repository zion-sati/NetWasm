using System;
using System.Collections.Generic;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Node;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel;

public sealed record ComponentCoreModuleMergeRequest(
    string ApplicationModulePath,
    string RuntimeModulePath,
    string EnvironmentModulePath,
    string OutputPath,
    ComponentTarget Target,
    string? HostModulePath = null,
    string? ManagedExecutableAdapterModulePath = null)
{
    // Which inline hints of the application and the runtime the merged module keeps.
    public WasmInlineAnnotationSelection ApplicationInlineAnnotations { get; init; } = WasmInlineAnnotationSelection.Preserve;
    public WasmInlineAnnotationSelection RuntimeInlineAnnotations { get; init; } = WasmInlineAnnotationSelection.Preserve;

    // Which exports the merged module keeps; all of them without a selection.
    public WasmExportSelection? Exports { get; init; }

    // Where a merge that runs in steps writes the modules between them.
    public ComponentCoreModuleMergeIntermediates? Intermediates { get; init; }
}

// wasm-merge takes its inputs as they are and keeps every export, so a merge through it
// selects each input's hints into a module of its own first, and the exports after it.
public sealed record ComponentCoreModuleMergeIntermediates(
    string ApplicationModulePath,
    string RuntimeModulePath,
    string MergedModulePath);

public interface IComponentCoreModuleMergeRunner
{
    void Run(ComponentCoreModuleMergeRequest request);
}

/// <summary>
/// Merges through wasm-merge, as the browser compiler's link plan does: each input's hints are
/// selected into the intermediate modules first, and the exports after the merge.
/// </summary>
public sealed class ComponentCoreModuleMergeRunner(
    IBinaryenToolRunner tools,
    IWasmCoreModuleInlineAnnotationEditor inlineAnnotations,
    IWasmCoreModuleExportEditor exports) : IComponentCoreModuleMergeRunner
{
    private readonly IBinaryenToolRunner _tools = tools ??
        throw new ArgumentNullException(nameof(tools));
    private readonly IWasmCoreModuleInlineAnnotationEditor _inlineAnnotations = inlineAnnotations ??
        throw new ArgumentNullException(nameof(inlineAnnotations));
    private readonly IWasmCoreModuleExportEditor _exports = exports ??
        throw new ArgumentNullException(nameof(exports));

    public void Run(ComponentCoreModuleMergeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var steps = request.Intermediates ??
            throw ComponentException.Invalid("a merge through wasm-merge needs its intermediate modules");
        _inlineAnnotations.Rewrite(request.ApplicationModulePath, steps.ApplicationModulePath,
            request.ApplicationInlineAnnotations);
        _inlineAnnotations.Rewrite(request.RuntimeModulePath, steps.RuntimeModulePath,
            request.RuntimeInlineAnnotations);
        var merged = request.Exports is null ? request.OutputPath : steps.MergedModulePath;
        Merge(request, steps.ApplicationModulePath, steps.RuntimeModulePath, merged);
        if (request.Exports is { } selection)
        {
            _exports.Rewrite(merged, request.OutputPath, selection);
        }
    }

    private void Merge(ComponentCoreModuleMergeRequest request, string application, string runtime, string output)
    {
        var arguments = new List<string>
        {
            runtime,
            "netwasm.runtime.v1",
            application,
            "netwasm.application.v1",
            request.EnvironmentModulePath,
            "env",
        };
        if (request.HostModulePath is not null)
        {
            arguments.Add(request.HostModulePath);
            arguments.Add(RuntimeAbi.HostModule);
        }
        if (request.ManagedExecutableAdapterModulePath is not null)
        {
            arguments.Add(request.ManagedExecutableAdapterModulePath);
            arguments.Add("netwasm.command.v1");
        }
        arguments.Add("--output");
        arguments.Add(output);
        arguments.Add("--enable-multimemory");
        arguments.Add("--enable-exception-handling");
        // catch_all_ref cleanup carries an exnref block result across the root release.
        arguments.Add("--enable-reference-types");
        arguments.Add("--enable-bulk-memory");
        arguments.Add("--enable-nontrapping-float-to-int");
        arguments.Add("--enable-simd");
        if (request.Target.Width == "wasm64")
        {
            arguments.Add("--enable-memory64");
        }
        var result = _tools.Run(BinaryenToolIds.WasmMerge, [.. arguments]);
        if (result.ExitCode == 0)
        {
            return;
        }
        var error = result.StandardError.Replace('\r', ' ')
            .Replace('\n', ' ').Trim();
        throw ComponentException.Tool(
            "wasm-merge failed to link the NetWasm runtime: " +
            (error.Length == 0 ? "unknown error" : error));
    }
}
