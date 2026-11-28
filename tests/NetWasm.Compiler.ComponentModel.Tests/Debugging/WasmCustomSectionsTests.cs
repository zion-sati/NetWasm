// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Debugging;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Debugging;

public sealed class WasmCustomSectionsTests
{
    [Fact]
    public void ReplacesTheSectionsWithThePrefixAndKeepsEveryOtherSection()
    {
        var module = DebugModuleBuilder.Module(DebugModuleBuilder.Op(DebugModuleBuilder.Nop));
        var withDwarf = WasmCustomSections.Replace(module, _ => false, [new(".debug_str", [9]), new(".debug_line", [8])]);
        Assert.Equal(module.Length + 2 * 2 + ".debug_str".Length + ".debug_line".Length + 2 * 2, withDwarf.Length);

        var replaced = WasmCustomSections.Replace(withDwarf, name => name.StartsWith(".debug_", StringComparison.Ordinal), [new(".debug_info", [7, 6])]);

        Assert.Equal(module, replaced.AsSpan(0, module.Length).ToArray());
        Assert.Equal([0, 14, 11, .. ".debug_info"u8, 7, 6], replaced.AsSpan(module.Length).ToArray());
        Assert.Equal(WasmCustomSections.Code(module), WasmCustomSections.Code(replaced));
    }

    [Fact]
    public void FindsTheCodeSectionsContents()
    {
        var module = DebugModuleBuilder.Module(DebugModuleBuilder.Op(DebugModuleBuilder.Nop));
        var (start, length) = WasmCustomSections.Code(module);

        Assert.Equal(10, module[start - 2]);
        Assert.Equal([1, 3, 0, DebugModuleBuilder.Nop, DebugModuleBuilder.End], module.AsSpan(start, length).ToArray());
        var empty = DebugModuleBuilder.Module(false);
        Assert.Equal((empty.Length, 0), WasmCustomSections.Code(empty));
    }

    [Fact]
    public void RejectsWhatIsNotACoreModule()
    {
        Assert.Throws<CompilerException>(() => WasmCustomSections.Code([1, 2, 3]));
        Assert.Throws<CompilerException>(() => WasmCustomSections.Replace([0, 0x61, 0x73, 0x6d, 2, 0, 0, 0], _ => true, []));
        Assert.Throws<CompilerException>(() => WasmCustomSections.Code([0, 0x61, 0x73, 0x6d, 1, 0, 0, 0, 1, 5, 0]));
        Assert.Throws<ArgumentNullException>(() => WasmCustomSections.Code(null!));
        Assert.Throws<ArgumentNullException>(() => WasmCustomSections.Replace(null!, _ => true, []));
        Assert.Throws<ArgumentNullException>(() => WasmCustomSections.Replace([], null!, []));
        Assert.Throws<ArgumentNullException>(() => WasmCustomSections.Replace([], _ => true, null!));
    }
}
