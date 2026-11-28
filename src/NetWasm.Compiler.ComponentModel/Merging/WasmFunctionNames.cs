// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;

namespace NetWasm.Compiler.ComponentModel.Merging;

// The function names of a module's name section, which the merger and the debug information writer read.
internal static class WasmFunctionNames
{
    // The function name subsection's entries; the module and local name subsections are not kept.
    public static List<(uint Function, string Name)> Read(byte[] bytes, WasmMergeRange range)
    {
        var result = new List<(uint, string)>();
        var at = range.Start;
        var end = range.Start + range.Length;
        while (at < end)
        {
            var id = WasmMergeRead.Byte(bytes, ref at);
            var size = (int)WasmMergeRead.Unsigned(bytes, ref at);
            var next = at + size;
            if (id == 1)
            {
                for (var count = WasmMergeRead.Unsigned(bytes, ref at); count > 0; count--)
                {
                    var function = WasmMergeRead.Unsigned(bytes, ref at);
                    result.Add((function, WasmMergeRead.Name(bytes, ref at)));
                }
            }
            at = next;
        }
        return result;
    }
}
