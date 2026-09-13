namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class EmscriptenEnvironmentShimWriterTests
{
    [Theory]
    [InlineData("wasm32", "i32")]
    [InlineData("wasm64", "i64")]
    public void EmitsOnlyTheTargetWidthMemoryGrowthNotificationFallback(
        string width,
        string addressType)
    {
        var modules = new RecordingModuleWriter();
        var target = new ComponentTarget(width, "0.2", "utf8");

        new EmscriptenEnvironmentShimWriter(modules).Write("environment.wasm", target);

        Assert.Equal("environment.wasm", modules.OutputPath);
        Assert.Contains("emscripten_notify_memory_growth", modules.Source);
        Assert.Contains($"(param {addressType})", modules.Source);
        Assert.DoesNotContain("memory", modules.Source.Replace(
            "emscripten_notify_memory_growth", "", StringComparison.Ordinal));
    }

    private sealed class RecordingModuleWriter : IWasmTextModuleWriter
    {
        public string Source { get; private set; } = "";
        public string OutputPath { get; private set; } = "";

        public void Write(string source, string outputPath)
        {
            Source = source;
            OutputPath = outputPath;
        }
    }
}
