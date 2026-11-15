// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeArtifactPublisher
{
    void PublishIfDifferent(string outputPath, byte[] bytes, string sha256);
}
