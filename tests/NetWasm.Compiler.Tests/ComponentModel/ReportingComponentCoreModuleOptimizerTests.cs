using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Linking;

namespace NetWasm.Compiler.Tests.ComponentModel;

// A wasm-opt run reports its module's size and level as it starts and its time as it ends, so a long run does not
// look like a stopped build (zion-sati/NetWasm#237).
public sealed class ReportingComponentCoreModuleOptimizerTests
{
    // Every level that runs wasm-opt reports, with its own flag.
    [Theory]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O2)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Os)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void AnOptimizingRunReportsItsStartAndItsTime(FinalWasmOptimization optimization)
    {
        using var directory = new TemporaryFile(2_500_000);
        var time = new SteppedTime();
        var optimizer = new RecordingOptimizer(() => time.Now += 3_500);
        var logger = new RecordingLogger();
        var reporting = new ReportingComponentCoreModuleOptimizer(optimizer, logger, time);

        reporting.Optimize(directory.Path, "out.wasm", ComponentTarget.Wasm32Wasi02, optimization,
            converge: true, lowMemoryUnused: true);

        Assert.Equal(
            [$"NetWasm: Optimizing the 2.5 MB module with wasm-opt -{optimization}...", "NetWasm: Optimized the module in 3.5s."],
            logger.Messages);
        Assert.Equal((directory.Path, "out.wasm", optimization, true, true), Assert.Single(optimizer.Calls));
    }

    // None and O0 run no wasm-opt passes, and a missing input is the optimizer's to report: nothing is logged.
    [Theory]
    [InlineData(FinalWasmOptimization.None, true)]
    [InlineData(FinalWasmOptimization.O0, true)]
    [InlineData(FinalWasmOptimization.Oz, false)]
    public void RunsWithoutPassesOrInputReportNothing(FinalWasmOptimization optimization, bool inputExists)
    {
        using var directory = new TemporaryFile(16);
        var input = inputExists ? directory.Path : directory.Path + ".missing";
        var optimizer = new RecordingOptimizer(() => { });
        var logger = new RecordingLogger();

        new ReportingComponentCoreModuleOptimizer(optimizer, logger, new SteppedTime())
            .Optimize(input, "out.wasm", ComponentTarget.Wasm32Wasi02, optimization);

        Assert.Empty(logger.Messages);
        Assert.Equal((input, "out.wasm", optimization, false, false), Assert.Single(optimizer.Calls));
    }

    [Fact]
    public void RequiresItsCollaboratorsAndIsTheRegisteredOptimizer()
    {
        var optimizer = new RecordingOptimizer(() => { });
        Assert.Throws<ArgumentNullException>("optimizer", () => new ReportingComponentCoreModuleOptimizer(null!, new RecordingLogger(), TimeProvider.System));
        Assert.Throws<ArgumentNullException>("logger", () => new ReportingComponentCoreModuleOptimizer(optimizer, null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>("time", () => new ReportingComponentCoreModuleOptimizer(optimizer, new RecordingLogger(), null!));
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        Assert.IsType<ReportingComponentCoreModuleOptimizer>(services.GetRequiredService<IComponentCoreModuleOptimizer>());
    }

    private sealed class RecordingOptimizer(Action whileRunning) : IComponentCoreModuleOptimizer
    {
        public List<(string Input, string Output, FinalWasmOptimization Optimization, bool Converge, bool LowMemoryUnused)> Calls { get; } = [];

        public void Optimize(string inputPath, string outputPath, ComponentTarget target, FinalWasmOptimization optimization,
            bool converge = false, bool lowMemoryUnused = false)
        {
            Calls.Add((inputPath, outputPath, optimization, converge, lowMemoryUnused));
            whileRunning();
        }
    }

    private sealed class RecordingLogger : ILogger<ReportingComponentCoreModuleOptimizer>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    // A clock in milliseconds that moves only when told to.
    private sealed class SteppedTime : TimeProvider
    {
        public long Now { get; set; }
        public override long TimestampFrequency => 1_000;
        public override long GetTimestamp() => Now;
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(int bytes)
        {
            Path = System.IO.Path.GetTempFileName();
            File.WriteAllBytes(Path, new byte[bytes]);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
