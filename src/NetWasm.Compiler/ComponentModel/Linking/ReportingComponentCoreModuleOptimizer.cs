using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace NetWasm.Compiler.ComponentModel.Linking;

// Reports a wasm-opt run as it starts and ends, so a long one does not look like a stopped build
// (zion-sati/NetWasm#237): Binaryen prints nothing while it runs, and its time depends on the module's largest
// functions and the host more than on the module's size. None and O0 run no passes, and a missing input is the
// optimizer's to report, so those pass straight through.
internal sealed class ReportingComponentCoreModuleOptimizer(
    IComponentCoreModuleOptimizer optimizer,
    ILogger<ReportingComponentCoreModuleOptimizer> logger,
    TimeProvider time) : IComponentCoreModuleOptimizer
{
    private static readonly Action<ILogger, double, FinalWasmOptimization, Exception?> LogStarted =
        LoggerMessage.Define<double, FinalWasmOptimization>(
            LogLevel.Information,
            new EventId(4003, "WasmOptimizationStarted"),
            "NetWasm: Optimizing the {Megabytes:F1} MB module with wasm-opt -{Optimization}...");

    private static readonly Action<ILogger, double, Exception?> LogFinished =
        LoggerMessage.Define<double>(
            LogLevel.Information,
            new EventId(4004, "WasmOptimizationFinished"),
            "NetWasm: Optimized the module in {Seconds:F1}s.");

    private readonly IComponentCoreModuleOptimizer _optimizer = optimizer ??
        throw new ArgumentNullException(nameof(optimizer));
    private readonly ILogger<ReportingComponentCoreModuleOptimizer> _logger = logger ??
        throw new ArgumentNullException(nameof(logger));
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));

    public void Optimize(
        string inputPath,
        string outputPath,
        ComponentTarget target,
        FinalWasmOptimization optimization,
        bool converge = false,
        bool lowMemoryUnused = false)
    {
        if (optimization is FinalWasmOptimization.None or FinalWasmOptimization.O0 || !File.Exists(inputPath))
        {
            _optimizer.Optimize(inputPath, outputPath, target, optimization, converge, lowMemoryUnused);
            return;
        }
        LogStarted(_logger, new FileInfo(inputPath).Length / 1_000_000d, optimization, null);
        var started = _time.GetTimestamp();
        _optimizer.Optimize(inputPath, outputPath, target, optimization, converge, lowMemoryUnused);
        LogFinished(_logger, _time.GetElapsedTime(started).TotalSeconds, null);
    }
}
