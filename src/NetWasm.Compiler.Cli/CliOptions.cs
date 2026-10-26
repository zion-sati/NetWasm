using System;
using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli;

internal sealed record ComponentizeCliOptions(
    string CoreModule,
    string? RuntimeModule,
    string Wit,
    string? World,
    string Output,
    string Manifest,
    WasmTarget Target,
    string? InteropManifest,
    string? JcoVersion,
    string? Preview2ShimVersion,
    FinalWasmOptimization Optimization)
{
    public string? ManagedInliningHints { get; init; }
    public static ComponentizeCliOptions Parse(string[] arguments)
    {
        string? coreModule = null;
        string? runtimeModule = null;
        string? wit = null;
        string? world = null;
        string? output = null;
        string? manifest = null;
        string? target = null;
        string? interopManifest = null;
        string? jcoVersion = null;
        string? preview2ShimVersion = null;
        string? optimization = null;
        string? managedInliningHints = null;
        CliOptionReader.Read(arguments, (option, value) =>
        {
            switch (option)
            {
                case "--core-module": coreModule = SetOnce(coreModule, value, option); break;
                case "--runtime-module": runtimeModule = SetOnce(runtimeModule, value, option); break;
                case "--wit": wit = SetOnce(wit, value, option); break;
                case "--world": world = SetOnce(world, value, option); break;
                case "--output": output = SetOnce(output, value, option); break;
                case "--manifest": manifest = SetOnce(manifest, value, option); break;
                case "--target": target = SetOnce(target, value, option); break;
                case "--interop-manifest": interopManifest = SetOnce(interopManifest, value, option); break;
                case "--jco-version": jcoVersion = SetOnce(jcoVersion, value, option); break;
                case "--preview2-shim-version": preview2ShimVersion = SetOnce(preview2ShimVersion, value, option); break;
                case "--optimization": optimization = SetOnce(optimization, value, option); break;
                case "--managed-inlining-hints": managedInliningHints = SetOnce(managedInliningHints, value, option); break;
                default:
                    throw CliOptionException.Create(
                    $"unknown componentize option '{option}'");
            }
        });
        return new ComponentizeCliOptions(
            coreModule ?? throw CliOptionException.Create("missing required option '--core-module'"),
            runtimeModule,
            wit ?? throw CliOptionException.Create("missing required option '--wit'"),
            world,
            output ?? throw CliOptionException.Create("missing required option '--output'"),
            manifest ?? throw CliOptionException.Create("missing required option '--manifest'"),
            ParseTarget(target),
            interopManifest,
            jcoVersion,
            preview2ShimVersion,
            ParseOptimization(optimization))
        { ManagedInliningHints = managedInliningHints };
    }

    private static WasmTarget ParseTarget(string? value) => value switch
    {
        null or "wasm32" => WasmTarget.Wasm32,
        "wasm64" => WasmTarget.Wasm64,
        _ => throw CliOptionException.Create("target must be 'wasm32' or 'wasm64'"),
    };

    private static FinalWasmOptimization ParseOptimization(string? value) => value switch
    {
        null or "Oz" => FinalWasmOptimization.Oz,
        "None" or "none" => FinalWasmOptimization.None,
        "O0" => FinalWasmOptimization.O0,
        "O1" => FinalWasmOptimization.O1,
        "O2" => FinalWasmOptimization.O2,
        "O3" => FinalWasmOptimization.O3,
        "Os" => FinalWasmOptimization.Os,
        _ => throw CliOptionException.Create(
            "optimization must be 'none', 'O0', 'O1', 'O2', 'O3', 'Os' or 'Oz'"),
    };

    private static string SetOnce(string? current, string value, string option) =>
        current is null
            ? value
            : throw CliOptionException.Create(
                $"option '{option}' was specified more than once");
}

internal sealed record CompileCliOptions(
    string Input,
    string Output,
    string EntryType,
    string EntryMethod,
    ImmutableArray<string> References,
    ImmutableArray<RequestedExport> Exports,
    WasmTarget Target,
    string? InteropManifest,
    string? RuntimeLayout,
    string? DiagnosticTrace,
    string? DiagnosticLog,
    string? StackTraceSymbols,
    string? Wit,
    string? World,
    ImmutableArray<string> Sources,
    CompilerEntryPointKind EntryPointKind = CompilerEntryPointKind.RawFunction,
    bool UseJavaScriptExportBoundary = false)
{
    public FinalWasmOptimization Optimization { get; init; } =
        FinalWasmOptimization.Oz;
    // Modules the input's native archives import from; the host supplies them.
    public ImmutableArray<string> ImportModules { get; init; } = [];
    public string? ManagedLoopInvariantFieldHoisting { get; init; }
    public string? ManagedStaticDataViewElimination { get; init; }
    public string? ManagedAddressTakenValuePromotion { get; init; }
    public string? ManagedMethodInlining { get; init; }
    public string? ManagedBranchPrefixDuplication { get; init; }
    public string? ManagedScalarCallPreservation { get; init; }
    public string? ManagedArrayBoundsCheckElimination { get; init; }
    public string? ManagedLoopUnrolling { get; init; }
    public string? ManagedConstantArgumentPropagation { get; init; }
    public string? ManagedStaticArrayPopulationElimination { get; init; }
    public string? ManagedConstantCallFolding { get; init; }
    public string? ManagedSwitchBitTests { get; init; }
    public string? ManagedDevirtualization { get; init; }
    public string? ManagedAllocationElimination { get; init; }
    public string? ManagedLoopRotation { get; init; }
    public string? ManagedArrayStoreCheckElimination { get; init; }

    public static CompileCliOptions Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string? input = null;
        string? output = null;
        string? entry = null;
        string? entryKind = null;
        string? target = null;
        string? interopManifest = null;
        string? runtimeLayout = null;
        string? diagnosticTrace = null;
        string? diagnosticLog = null;
        string? stackTraceSymbols = null;
        string? wit = null;
        string? world = null;
        string? javaScriptExportBoundary = null;
        string? optimization = null;
        string? managedLoopInvariantFieldHoisting = null;
        string? managedStaticDataViewElimination = null;
        string? managedAddressTakenValuePromotion = null;
        string? managedMethodInlining = null;
        string? managedBranchPrefixDuplication = null;
        string? managedScalarCallPreservation = null;
        string? managedArrayBoundsCheckElimination = null;
        string? managedLoopUnrolling = null;
        string? managedConstantArgumentPropagation = null;
        string? managedStaticArrayPopulationElimination = null;
        string? managedConstantCallFolding = null;
        string? managedSwitchBitTests = null;
        string? managedDevirtualization = null;
        string? managedAllocationElimination = null;
        string? managedLoopRotation = null;
        string? managedArrayStoreCheckElimination = null;
        var references = ImmutableArray.CreateBuilder<string>();
        var exports = ImmutableArray.CreateBuilder<RequestedExport>();
        var sources = ImmutableArray.CreateBuilder<string>();
        var importModules = ImmutableArray.CreateBuilder<string>();
        CliOptionReader.Read(arguments, (option, value) =>
        {
            switch (option)
            {
                case "--input": input = SetOnce(input, value, option); break;
                case "--output": output = SetOnce(output, value, option); break;
                case "--entry": entry = SetOnce(entry, value, option); break;
                case "--entry-kind": entryKind = SetOnce(entryKind, value, option); break;
                case "--target": target = SetOnce(target, value, option); break;
                case "--interop-manifest": interopManifest = SetOnce(interopManifest, value, option); break;
                case "--runtime-layout": runtimeLayout = SetOnce(runtimeLayout, value, option); break;
                case "--diagnostic-trace": diagnosticTrace = SetOnce(diagnosticTrace, value, option); break;
                case "--diagnostic-log": diagnosticLog = SetOnce(diagnosticLog, value, option); break;
                case "--stack-trace-symbols": stackTraceSymbols = SetOnce(stackTraceSymbols, value, option); break;
                case "--wit": wit = SetOnce(wit, value, option); break;
                case "--world": world = SetOnce(world, value, option); break;
                case "--javascript-export-boundary":
                    javaScriptExportBoundary =
                    SetOnce(javaScriptExportBoundary, value, option); break;
                case "--optimization":
                    optimization = SetOnce(optimization, value, option); break;
                case "--managed-loop-invariant-field-hoisting":
                    managedLoopInvariantFieldHoisting = SetOnce(
                        managedLoopInvariantFieldHoisting,
                        value,
                        option);
                    break;
                case "--managed-static-data-view-elimination":
                    managedStaticDataViewElimination = SetOnce(
                        managedStaticDataViewElimination,
                        value,
                        option);
                    break;
                case "--managed-address-taken-value-promotion":
                    managedAddressTakenValuePromotion = SetOnce(
                        managedAddressTakenValuePromotion,
                        value,
                        option);
                    break;
                case "--managed-method-inlining":
                    managedMethodInlining = SetOnce(
                        managedMethodInlining,
                        value,
                        option);
                    break;
                case "--managed-branch-prefix-duplication":
                    managedBranchPrefixDuplication = SetOnce(
                        managedBranchPrefixDuplication,
                        value,
                        option);
                    break;
                case "--managed-scalar-call-preservation":
                    managedScalarCallPreservation = SetOnce(
                        managedScalarCallPreservation,
                        value,
                        option);
                    break;
                case "--managed-array-bounds-check-elimination":
                    managedArrayBoundsCheckElimination = SetOnce(managedArrayBoundsCheckElimination, value, option);
                    break;
                case "--managed-loop-unrolling":
                    managedLoopUnrolling = SetOnce(managedLoopUnrolling, value, option);
                    break;
                case "--managed-constant-argument-propagation":
                    managedConstantArgumentPropagation = SetOnce(managedConstantArgumentPropagation, value, option);
                    break;
                case "--managed-static-array-population-elimination":
                    managedStaticArrayPopulationElimination = SetOnce(managedStaticArrayPopulationElimination, value, option);
                    break;
                case "--managed-constant-call-folding":
                    managedConstantCallFolding = SetOnce(managedConstantCallFolding, value, option);
                    break;
                case "--managed-switch-bit-tests":
                    managedSwitchBitTests = SetOnce(managedSwitchBitTests, value, option);
                    break;
                case "--managed-devirtualization":
                    managedDevirtualization = SetOnce(managedDevirtualization, value, option);
                    break;
                case "--managed-allocation-elimination":
                    managedAllocationElimination = SetOnce(managedAllocationElimination, value, option);
                    break;
                case "--managed-loop-rotation":
                    managedLoopRotation = SetOnce(managedLoopRotation, value, option);
                    break;
                case "--managed-array-store-check-elimination":
                    managedArrayStoreCheckElimination = SetOnce(managedArrayStoreCheckElimination, value, option);
                    break;
                case "--reference": references.Add(value); break;
                case "--export": exports.Add(ParseExport(value)); break;
                case "--source": sources.Add(value); break;
                case "--import-module": importModules.Add(value); break;
                default: throw CliOptionException.Create($"unknown option '{option}'");
            }
        });

        var selectedEntryKind = entryKind switch
        {
            null or "raw-function" => CompilerEntryPointKind.RawFunction,
            "managed-executable" => CompilerEntryPointKind.ManagedExecutable,
            "library" => CompilerEntryPointKind.Library,
            _ => throw CliOptionException.Create(
                "entry kind must be 'raw-function', 'managed-executable' or 'library'"),
        };
        if (selectedEntryKind == CompilerEntryPointKind.Library && entry is not null)
        {
            throw CliOptionException.Create("library compilation cannot specify '--entry'");
        }
        (var entryType, var entryMethod) = selectedEntryKind == CompilerEntryPointKind.Library
            ? (string.Empty, string.Empty)
            : ParseMethod(entry ?? throw CliOptionException.Create("missing required option '--entry'"));
        return new CompileCliOptions(
            input ?? throw CliOptionException.Create("missing required option '--input'"),
            output ?? throw CliOptionException.Create("missing required option '--output'"),
            entryType,
            entryMethod,
            references.ToImmutable(),
            exports.ToImmutable(),
            target switch
            {
                null or "wasm32" => WasmTarget.Wasm32,
                "wasm64" => WasmTarget.Wasm64,
                _ => throw CliOptionException.Create("target must be 'wasm32' or 'wasm64'"),
            },
            interopManifest,
            runtimeLayout,
            diagnosticTrace,
            diagnosticLog,
            stackTraceSymbols,
            wit,
            world,
            sources.ToImmutable(),
            selectedEntryKind,
            javaScriptExportBoundary switch
            {
                null or "false" => false,
                "true" => true,
                _ => throw CliOptionException.Create(
                    "javascript export boundary must be 'true' or 'false'"),
            })
        {
            Optimization = ParseOptimization(optimization),
            ManagedLoopInvariantFieldHoisting = managedLoopInvariantFieldHoisting,
            ManagedStaticDataViewElimination = managedStaticDataViewElimination,
            ManagedAddressTakenValuePromotion = managedAddressTakenValuePromotion,
            ManagedMethodInlining = managedMethodInlining,
            ManagedBranchPrefixDuplication = managedBranchPrefixDuplication,
            ManagedScalarCallPreservation = managedScalarCallPreservation,
            ManagedArrayBoundsCheckElimination = managedArrayBoundsCheckElimination,
            ManagedLoopUnrolling = managedLoopUnrolling,
            ManagedConstantArgumentPropagation = managedConstantArgumentPropagation,
            ManagedStaticArrayPopulationElimination = managedStaticArrayPopulationElimination,
            ManagedConstantCallFolding = managedConstantCallFolding,
            ManagedSwitchBitTests = managedSwitchBitTests,
            ImportModules = importModules.ToImmutable(),
            ManagedDevirtualization = managedDevirtualization,
            ManagedAllocationElimination = managedAllocationElimination,
            ManagedLoopRotation = managedLoopRotation,
            ManagedArrayStoreCheckElimination = managedArrayStoreCheckElimination,
        };
    }

    private static RequestedExport ParseExport(string value)
    {
        var separator = value.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw CliOptionException.Create(
                "export must use 'name=Namespace.Type::Method' syntax");
        }
        var name = value[..separator];
        var (type, method) = ParseMethod(value[(separator + 1)..]);
        return new RequestedExport(name, type, method);
    }

    private static (string Type, string Method) ParseMethod(string value)
    {
        var parts = value.Split("::", 2, StringSplitOptions.None);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw CliOptionException.Create(
                "method must use 'Namespace.Type::Method' syntax");
        }
        return (parts[0], parts[1]);
    }

    private static FinalWasmOptimization ParseOptimization(string? value) => value switch
    {
        null or "Oz" => FinalWasmOptimization.Oz,
        "None" or "none" => FinalWasmOptimization.None,
        "O0" => FinalWasmOptimization.O0,
        "O1" => FinalWasmOptimization.O1,
        "O2" => FinalWasmOptimization.O2,
        "O3" => FinalWasmOptimization.O3,
        "Os" => FinalWasmOptimization.Os,
        _ => throw CliOptionException.Create(
            "optimization must be 'none', 'O0', 'O1', 'O2', 'O3', 'Os' or 'Oz'"),
    };

    private static string SetOnce(string? current, string value, string option) =>
        current is null
            ? value
            : throw CliOptionException.Create(
                $"option '{option}' was specified more than once");
}

internal static class CliOptionReader
{
    public static void Read(string[] arguments, Action<string, string> read)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(read);
        for (var index = 0; index < arguments.Length; index++)
        {
            var option = arguments[index];
            var value = index + 1 < arguments.Length
                ? arguments[++index]
                : throw CliOptionException.Create($"option '{option}' requires a value");
            read(option, value);
        }
    }

}
