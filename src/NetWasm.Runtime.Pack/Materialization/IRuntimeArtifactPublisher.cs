namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeArtifactPublisher
{
    void PublishIfDifferent(string outputPath, byte[] bytes, string sha256);
}
