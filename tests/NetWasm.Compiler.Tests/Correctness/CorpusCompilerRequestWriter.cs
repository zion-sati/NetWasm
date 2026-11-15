// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusCompilerRequestWriter
{
    void Write(string path, CorpusCompilerRequest request);
}

internal sealed class CorpusCompilerRequestWriter : ICorpusCompilerRequestWriter
{
    public void Write(string path, CorpusCompilerRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);
        File.WriteAllText(path, JsonSerializer.Serialize(request));
    }
}
