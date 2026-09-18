using System;
using System.Diagnostics;
using NetWasm.Compiler.Browser.Results;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Metadata.ManagedExecutables;

namespace NetWasm.Compiler.Browser;

internal interface IBrowserCompilationCommand
{
    BrowserCompilationResult Compile(PreparedBrowserCompilation preparation);
}

internal sealed class BrowserCompilationCommand(
    INetWasmCompiler compiler,
    IManagedExecutableEntryPointSelector entryPoints,
    IBrowserCompilationResultProjector results) : IBrowserCompilationCommand
{
    public BrowserCompilationResult Compile(PreparedBrowserCompilation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var request = preparation.Request;
        var collectMetrics = request.CollectCompilerMetrics || request.Options.MetricsObserver is not null;
        var adapterStarted = collectMetrics ? Stopwatch.GetTimestamp() : 0;
        var metrics = collectMetrics
            ? new BrowserCompilerMetricsObserver(request.Options.MetricsObserver)
            : null;
        var options = preparation.Options with { MetricsObserver = metrics };
        var image = preparation.EntryImage;

        var compilation = compiler.Compile(options);
        var actualEntry = image is null ? null : entryPoints.SelectEntryPoint(
            options.EntryAssemblyPath, image, compilation.Program.EntryPoint.Key.MetadataToken);
        if (actualEntry is not null)
        {
            options = options with
            {
                EntryTypeName = actualEntry.TypeName,
                EntryMethodName = actualEntry.MethodName,
                EntryMethodToken = actualEntry.MetadataToken,
            };
        }

        var projected = results.Project(compilation, options, actualEntry?.Abi);
        if (metrics is null) return projected;
        var compilerMetrics = metrics.Report ?? throw new InvalidOperationException(
            "compiler produced no metrics report");
        var totalDuration = Stopwatch.GetElapsedTime(adapterStarted);
        return projected with
        {
            CompilerMetrics = compilerMetrics,
            CompilerTiming = new(
                totalDuration,
                compilerMetrics.TotalDuration,
                totalDuration - compilerMetrics.TotalDuration),
        };
    }
}

internal sealed record PreparedBrowserCompilation(
    BrowserCompilationRequest Request,
    CompilerOptions Options,
    byte[]? EntryImage);

internal interface IBrowserCompilationPreparationFactory
{
    PreparedBrowserCompilation Prepare(BrowserCompilationRequest request);
}

internal sealed class BrowserCompilationPreparationFactory(
    IManagedAssemblyImageReader images,
    IManagedExecutableEntryPointSelector entryPoints) :
    IBrowserCompilationPreparationFactory
{
    public PreparedBrowserCompilation Prepare(BrowserCompilationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = request.Options;
        var image = options.EntryPointKind == CompilerEntryPointKind.ManagedExecutable
            ? images.Read(options.EntryAssemblyPath)
            : null;
        if (request.SelectManagedExecutableEntryPoint)
        {
            var selected = entryPoints.SelectEntryPoint(options.EntryAssemblyPath, image!);
            options = options with
            {
                EntryTypeName = selected.TypeName,
                EntryMethodName = selected.MethodName,
                EntryMethodToken = selected.MetadataToken,
            };
        }
        return new(request, options, image);
    }
}
