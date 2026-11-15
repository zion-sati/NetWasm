// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Tests.ManagedExecutables;

internal sealed class RecordingWasmTextModuleWriter : IWasmTextModuleWriter
{
    public string Source { get; private set; } = string.Empty;

    public string OutputPath { get; private set; } = string.Empty;

    public void Write(string source, string outputPath)
    {
        Source = source;
        OutputPath = outputPath;
    }
}
