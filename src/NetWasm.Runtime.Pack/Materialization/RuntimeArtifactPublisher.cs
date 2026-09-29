using System;
using System.IO;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeArtifactPublisher(
    IArtifactDigestCalculator artifactDigests) : IRuntimeArtifactPublisher
{
    public void PublishIfDifferent(string outputPath, byte[] bytes, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(bytes);
        var fullOutputPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullOutputPath))
        {
            var information = new FileInfo(fullOutputPath);
            if (information.Length == bytes.LongLength &&
                string.Equals(
                    artifactDigests.Calculate(fullOutputPath),
                    sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var directory = Path.GetDirectoryName(fullOutputPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullOutputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
