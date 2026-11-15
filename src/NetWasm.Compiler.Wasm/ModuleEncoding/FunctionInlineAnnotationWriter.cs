// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.ModuleEncoding;

public sealed class FunctionInlineAnnotationWriter(
    IUnsignedLeb128Encoder unsignedEncoder,
    IUtf8StringEncoder utf8,
    IWasmSectionWriter sections,
    Func<WasmBinaryBuffer, IWasmBinaryWriter> writers,
    Func<WasmBinaryBuffer, IWasmBinarySnapshotReader> snapshots) : IFunctionInlineAnnotationWriter
{
    private readonly IUnsignedLeb128Encoder _unsigned = unsignedEncoder ?? throw new ArgumentNullException(nameof(unsignedEncoder));
    private readonly IUtf8StringEncoder _utf8 = utf8 ?? throw new ArgumentNullException(nameof(utf8));
    private readonly IWasmSectionWriter _sections = sections ?? throw new ArgumentNullException(nameof(sections));
    private readonly Func<WasmBinaryBuffer, IWasmBinaryWriter> _writers = writers ?? throw new ArgumentNullException(nameof(writers));
    private readonly Func<WasmBinaryBuffer, IWasmBinarySnapshotReader> _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));

    public void Write(IWasmBinaryWriter output, IReadOnlyList<WasmFunctionDefinition> functions, int functionImportCount)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(functionImportCount);
        var count = functions.Count(function => function.IsNoInlining || function.IsAggressiveInlining);
        if (count == 0)
            return;

        var buffer = new WasmBinaryBuffer();
        var payload = _writers(buffer);
        _utf8.Encode(payload, "binaryen.inline");
        _unsigned.Encode(payload, (uint)count);
        for (var index = 0; index < functions.Count; index++)
        {
            var function = functions[index];
            if (!function.IsNoInlining && !function.IsAggressiveInlining)
                continue;

            _unsigned.Encode(payload, (uint)functionImportCount + (uint)index);
            _unsigned.Encode(payload, 1); // One annotation for this function.
            _unsigned.Encode(payload, 0); // Function-level offset, not an instruction.
            _unsigned.Encode(payload, 1); // One signed-seven-bit hint byte.
            payload.Write([function.IsNoInlining ? (byte)0 : (byte)127]);
        }
        _sections.Write(output, 0, _snapshots(buffer).Read());
    }
}
