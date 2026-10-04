using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission.NativeInterop;

namespace NetWasm.Compiler.Cli;

internal sealed class CompileCliArtifactPlanner(
    INativeCallbackSupportArtifactValidator callbackSupport) :
    ICompileCliArtifactPlanner
{
    private readonly INativeCallbackSupportArtifactValidator _callbackSupport =
        callbackSupport ?? throw new ArgumentNullException(nameof(callbackSupport));

    public CompileCliArtifactPlan Plan(
        CompileCliOptions options,
        CompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(result);
        if (result.NativeImports.IsDefault)
        {
            throw new InvalidOperationException(
                "The compiler native import facts are uninitialized.");
        }

        _callbackSupport.Validate(result.NativeCallbackSupport);
        if (result.NativeCallbackSupport is not null && options.RuntimeLayout is null)
        {
            throw CliOptionException.Create(
                "native callbacks require option '--runtime-layout'");
        }

        var stackTraceSymbols = options.StackTraceSymbols is null
            ? null
            : result.StackTraceSymbols ?? throw new InvalidOperationException(
                "stack-trace instrumentation did not produce a symbol sidecar");
        var callbackObjectPath = Path.ChangeExtension(options.Output, ".callbacks.o");
        ValidateDistinctPaths(options, callbackObjectPath);

        var binarySidecars = ImmutableArray.CreateBuilder<BinaryCliArtifact>();
        if (result.NativeCallbackSupport is { } support)
        {
            binarySidecars.Add(new(callbackObjectPath, support.ObjectBytes));
        }
        if (stackTraceSymbols is not null)
        {
            binarySidecars.Add(new(options.StackTraceSymbols!, stackTraceSymbols.Bytes));
        }

        var textSidecars = ImmutableArray.CreateBuilder<TextCliArtifact>();
        if (options.RuntimeLayout is not null)
        {
            textSidecars.Add(new(
                options.RuntimeLayout,
                CreateRuntimeLayout(options, result, callbackObjectPath)));
        }
        if (options.InteropManifest is not null)
        {
            textSidecars.Add(new(
                options.InteropManifest,
                CliJson.Serialize(result.InteropManifest)));
        }

        return new(
            options.Output,
            result.ApplicationModule,
            callbackObjectPath,
            result.NativeCallbackSupport is null,
            binarySidecars.ToImmutable(),
            textSidecars.ToImmutable());
    }

    private static string CreateRuntimeLayout(
        CompileCliOptions options,
        CompilationResult result,
        string callbackObjectPath)
    {
        var target = options.Target == WasmTarget.Wasm64 ? "wasm64" : "wasm32";
        var nativeImports = result.NativeImports.Select(import => new
        {
            import.LibraryName,
            import.EntryPoint,
            parameters = import.Parameters.Select(ToJsonValue).ToArray(),
            returnType = import.ReturnType is { } returnType
                ? ToJsonValue(returnType)
                : null,
        }).ToArray();
        var runtimeFeatures = result.RuntimeFeatures
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (result.NativeCallbackSupport is not { } support)
        {
            return CliJson.Serialize(new
            {
                schemaVersion = 3,
                target,
                applicationStaticDataEnd = result.StaticDataEnd,
                runtimeFeatures,
                nativeImports,
            });
        }

        return CliJson.Serialize(new
        {
            schemaVersion = 4,
            target,
            applicationStaticDataEnd = result.StaticDataEnd,
            runtimeFeatures,
            nativeImports,
            nativeCallbackSupport = new
            {
                fileName = Path.GetFileName(callbackObjectPath),
                support.Sha256,
                callbacks = support.Callbacks.Select(callback => new
                {
                    callback.NativeSymbol,
                    callback.RuntimeImportSymbol,
                    callback.ApplicationExportName,
                    callback.RuntimeGetterExportName,
                    parameters = callback.Parameters.Select(ToJsonValue).ToArray(),
                    returnType = callback.ReturnType is { } returnType
                        ? ToJsonValue(returnType)
                        : null,
                }).ToArray(),
                support.TemporaryApplicationExports,
                support.TemporaryRuntimeExports,
            },
        });
    }

    private static string ToJsonValue(WasmValueType value) =>
        value.ToString().ToLowerInvariant();

    private static void ValidateDistinctPaths(
        CompileCliOptions options,
        string callbackObjectPath)
    {
        var paths = new[]
        {
            options.Output,
            callbackObjectPath,
            options.RuntimeLayout,
            options.InteropManifest,
            options.StackTraceSymbols,
        };
        // Sidecars may be copied between case-sensitive and case-insensitive hosts.
        // Reject case-only aliases everywhere so one artifact can never overwrite
        // another after such a move.
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (path is null)
            {
                continue;
            }
            if (!distinct.Add(Path.GetFullPath(path)))
            {
                throw CliOptionException.Create(
                    "compiler output and sidecar paths must be distinct");
            }
        }
    }
}
