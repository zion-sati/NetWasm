// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ComponentModel.Inlining;

public sealed class WasmCoreModuleInlineAnnotationEditor(
    IByteFileReader reader,
    IByteFileWriter writer,
    IWasmInlineAnnotationFilter filter) : IWasmCoreModuleInlineAnnotationEditor
{
    private readonly IByteFileReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private readonly IByteFileWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly IWasmInlineAnnotationFilter _filter = filter ?? throw new ArgumentNullException(nameof(filter));

    public void Rewrite(string inputPath, string outputPath, WasmInlineAnnotationSelection selection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Enum.IsDefined(selection))
            throw new ArgumentOutOfRangeException(nameof(selection));
        var result = _filter.Filter(_reader.Read(inputPath), selection);
        _writer.Write(outputPath, result);
    }
}
