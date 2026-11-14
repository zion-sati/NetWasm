using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

// The component step's wasm-opt start and end reach the build output at high importance (zion-sati/NetWasm#237).
public sealed class MsBuildProgressLoggerProviderTests
{
    [Theory]
    [InlineData(MsBuildProgressLoggerProvider.OptimizationCategory, LogLevel.Information, true)]
    [InlineData(MsBuildProgressLoggerProvider.OptimizationCategory, LogLevel.Debug, false)]
    [InlineData("NetWasm.Compiler.Pipeline.CompilationPipelineExecutor", LogLevel.Information, false)]
    [InlineData(null, LogLevel.Information, false)]
    public void ShowsOnlyTheOptimizationProgress(string? category, LogLevel level, bool expected) =>
        Assert.Equal(expected, MsBuildProgressLoggerProvider.Shows(category, level));

    [Fact]
    public void WritesAnEntryAsAHighImportanceMessage()
    {
        var engine = new MessageEngine();
        using var provider = new MsBuildProgressLoggerProvider(new TaskLoggingHelper(engine, "NetWasmComponentize"));
        var logger = provider.CreateLogger(MsBuildProgressLoggerProvider.OptimizationCategory);

        logger.Log(LogLevel.Information, default, 1.5, null, static (seconds, _) => $"NetWasm: Optimized the module in {seconds:F1}s.");

        var message = Assert.Single(engine.Messages);
        Assert.Equal(MessageImportance.High, message.Importance);
        Assert.Equal("NetWasm: Optimized the module in 1.5s.", message.Message);
        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.Null(logger.BeginScope("scope"));
        Assert.Throws<ArgumentNullException>(() => new MsBuildProgressLoggerProvider(null!));
    }

    private sealed class MessageEngine : IBuildEngine
    {
        public List<BuildMessageEventArgs> Messages { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;
        public void LogErrorEvent(BuildErrorEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public bool BuildProjectFile(string projectFileName, string[] targetNames,
            System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => true;
    }
}
