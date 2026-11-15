// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.IO;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeArtifactPublisher(
    IArtifactDigestCalculator artifactDigests,
    IRuntimeArtifactWriter writer) : IRuntimeArtifactPublisher
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

        writer.Write(fullOutputPath, bytes);
    }
}
