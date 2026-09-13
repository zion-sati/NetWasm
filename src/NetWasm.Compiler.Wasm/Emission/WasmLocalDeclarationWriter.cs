using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal static class WasmLocalDeclarationWriter
{
    private static readonly UnsignedLeb128Encoder CountEncoder =
        new UnsignedLeb128Encoder();

    public static void Write(
        IWasmBinaryWriter code,
        IReadOnlyList<CliValueKind> localTypes,
        WasmTargetLayout target)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(localTypes);
        ArgumentNullException.ThrowIfNull(target);

        if (localTypes.Count == 0)
        {
            CountEncoder.Encode(code, 0);
            return;
        }

        var groups = new List<(int Count, WasmValueType Type)>();
        foreach (var localType in localTypes)
        {
            var type = WasmValueTypes.FromCli(localType, target);
            if (groups.Count != 0 && groups[^1].Type == type)
            {
                var previous = groups[^1];
                groups[^1] = (checked(previous.Count + 1), type);
                continue;
            }
            groups.Add((1, type));
        }

        CountEncoder.Encode(code, (uint)groups.Count);
        foreach (var group in groups)
        {
            CountEncoder.Encode(code, (uint)group.Count);
            code.Write([(byte)group.Type]);
        }
    }
}
