using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeLinkExportPlanBuilder : IRuntimeLinkExportPlanBuilder
{
    private const string EphemeronHandlesFeature = "ephemeron-handles";
    private const string StructuredCommandDiagnosticsFeature =
        "structured-command-diagnostics";
    private static readonly ImmutableArray<string> EphemeronExports =
    [
        "ephemeron_handle_get_key",
        "ephemeron_handle_get_value",
        "ephemeron_handle_new",
        "ephemeron_handle_release",
    ];
    private static readonly ImmutableArray<string> StructuredCommandDiagnosticsExports =
    [
        "command_exception_capture",
        "command_exception_completion",
        "command_exception_release",
        "command_exception_write",
    ];
    private static readonly ImmutableArray<string> RequiredRuntimeExports =
        ["emscripten_stack_get_current", "_emscripten_stack_restore"];
    private static readonly ImmutableArray<string> OptionalRuntimeExports =
        ["__start_em_asm", "__stop_em_asm", "__start_em_lib_deps", "__stop_em_lib_deps", "__start_em_js", "__stop_em_js"];
    private static readonly ImmutableArray<RuntimeLinkExport> InspectionExports =
        [new("__global_base", 3), new("__data_end", 3), new("__stack_low", 3), new("__stack_high", 3), new("__heap_base", 3)];

    public RuntimeLinkExportPlan Build(RuntimeLinkExportPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PublicExports.IsDefault || request.NativeBindings.IsDefault ||
            request.PublicExports.Any(InvalidName) || request.NativeBindings.Any(binding =>
                binding?.Import is null || InvalidName(binding.Import.EntryPoint)) ||
            request.NativeCallbackSupport is { } callbackSupport &&
            (callbackSupport.TemporaryRuntimeExports.IsDefault ||
             callbackSupport.TemporaryRuntimeExports.Any(InvalidName)))
            throw new InvalidOperationException("The runtime export plan inputs are invalid.");
        var publicExports = SelectPublicExports(request);
        var required = RequiredRuntimeExports.AddRange(publicExports);
        var arguments = RequiredRuntimeExports.Select(name => "--export=" + name)
            .Concat(OptionalRuntimeExports.Select(name => "--export-if-defined=" + name))
            .Concat(publicExports.Select(name => "--export=" + name)).ToImmutableArray();
        if (request.NativeBindings.IsEmpty && request.NativeCallbackSupport is null)
            return new(arguments, []);
        var publicNames = required.Concat(OptionalRuntimeExports).Append("memory").Append("__indirect_function_table")
            .ToHashSet(StringComparer.Ordinal);
        var callbackExports = request.NativeCallbackSupport?.TemporaryRuntimeExports ?? [];
        if (callbackExports.Any(publicNames.Contains))
            throw new InvalidOperationException("A native callback getter collides with a public runtime export.");
        var existing = InspectionExports
            .AddRange(request.NativeBindings.Select(binding =>
                new RuntimeLinkExport(binding.Import.EntryPoint, 0)));
        if (callbackExports.Any(name => existing.Any(export => export.Name == name)))
            throw new InvalidOperationException("A native callback getter collides with another runtime export.");
        var added = existing
            .AddRange(callbackExports.Select(name => new RuntimeLinkExport(name, 0)));
        if (added.GroupBy(export => export.Name, StringComparer.Ordinal)
            .Any(group => group.Select(export => export.Kind).Distinct().Count() != 1))
            throw new InvalidOperationException("The runtime export plan contains conflicting native export kinds.");
        return new(arguments.AddRange(added.Select(export => "--export=" + export.Name)),
            added.Distinct().Where(export => !publicNames.Contains(export.Name)).ToImmutableArray());
    }

    private static ImmutableArray<string> SelectPublicExports(
        RuntimeLinkExportPlanRequest request)
    {
        // Missing feature evidence is the compatibility form used by older
        // compilers. It retains every optional runtime capability.
        var includeEphemerons = request.RuntimeFeatures.IsDefault ||
            request.RuntimeFeatures.Contains(EphemeronHandlesFeature);
        var includeStructuredDiagnostics = request.RuntimeFeatures.IsDefault ||
            request.RuntimeFeatures.Contains(StructuredCommandDiagnosticsFeature);
        var exports = includeEphemerons
            ? request.PublicExports
            : request.PublicExports
                .Where(name => !EphemeronExports.Contains(name))
                .ToImmutableArray();
        if (includeStructuredDiagnostics)
        {
            exports = exports.AddRange(StructuredCommandDiagnosticsExports
                .Where(name => !exports.Contains(name)));
        }
        return exports;
    }

    private static bool InvalidName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['\0', '\r', '\n']) >= 0;
}
