using System;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.Browser.Inputs;
using NetWasm.Compiler.Browser.Results;
using NetWasm.Compiler.Browser.Wit;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Metadata.ManagedExecutables;

namespace NetWasm.Compiler.Browser;

/// <summary>
/// Composes the compiler with caller-owned virtual inputs. CompilerException
/// diagnostics propagate to the caller without translation.
/// </summary>
public static class BrowserCompiler
{
    public static BrowserCompilationResult Compile(BrowserCompilationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var collectMetrics = request.CollectCompilerMetrics || request.Options.MetricsObserver is not null;
        var adapterStarted = collectMetrics ? Stopwatch.GetTimestamp() : 0;
        var registrations = new ServiceCollection().AddNetWasmCompiler();
        registrations.AddSingleton<IManagedAssemblyImageReader>(
            new VirtualManagedAssemblyImageReader(request.Inputs));
        registrations.AddSingleton<ICompilationInputHasher>(
            new VirtualCompilationInputHasher(request.Inputs));
        registrations.AddSingleton<IWitDocumentReader>(
            new VirtualWitDocumentReader(request.NormalizedWitDocuments, new WitDocumentJsonReader()));
        registrations.AddSingleton<IBrowserCompilationResultProjector, BrowserCompilationResultProjector>();
        registrations.AddSingleton<IManagedExecutableEntryPointSelector, ManagedExecutableEntryPointSelector>();
        var metrics = collectMetrics
            ? new BrowserCompilerMetricsObserver(request.Options.MetricsObserver)
            : null;
        BrowserCompilationResult projected;
        CompilerMetricsReport? compilerMetrics;
        using (var services = registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }))
        {
            var options = request.Options with { MetricsObserver = metrics };
            var entries = services.GetRequiredService<IManagedExecutableEntryPointSelector>();
            var image = options.EntryPointKind == CompilerEntryPointKind.ManagedExecutable
                ? services.GetRequiredService<IManagedAssemblyImageReader>().Read(options.EntryAssemblyPath)
                : null;
            if (request.SelectManagedExecutableEntryPoint)
            {
                var selected = entries.SelectEntryPoint(options.EntryAssemblyPath, image!);
                options = options with
                {
                    EntryTypeName = selected.TypeName,
                    EntryMethodName = selected.MethodName,
                    EntryMethodToken = selected.MetadataToken,
                };
            }

            var result = services.GetRequiredService<INetWasmCompiler>().Compile(options);
            var actualEntry = image is null ? null : entries.SelectEntryPoint(
                options.EntryAssemblyPath, image, result.Program.EntryPoint.Key.MetadataToken);
            if (actualEntry is not null)
            {
                options = options with
                {
                    EntryTypeName = actualEntry.TypeName,
                    EntryMethodName = actualEntry.MethodName,
                    EntryMethodToken = actualEntry.MetadataToken,
                };
            }

            projected = services.GetRequiredService<IBrowserCompilationResultProjector>().Project(
                result, options, actualEntry?.Abi);
            compilerMetrics = metrics?.Report;
        }
        if (metrics is null)
        {
            return projected;
        }

        var completedCompilerMetrics = compilerMetrics ?? throw new InvalidOperationException(
            "compiler produced no metrics report");
        var totalDuration = Stopwatch.GetElapsedTime(adapterStarted);
        return projected with
        {
            CompilerMetrics = completedCompilerMetrics,
            CompilerTiming = new(
                totalDuration,
                completedCompilerMetrics.TotalDuration,
                totalDuration - completedCompilerMetrics.TotalDuration),
        };
    }
}
