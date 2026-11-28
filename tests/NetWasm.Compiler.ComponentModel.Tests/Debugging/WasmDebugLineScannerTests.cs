// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel.Debugging;
using NetWasm.Compiler.ComponentModel.Merging;
using static NetWasm.Compiler.ComponentModel.Tests.Debugging.DebugModuleBuilder;

namespace NetWasm.Compiler.ComponentModel.Tests.Debugging;

public sealed class WasmDebugLineScannerTests
{
    private static readonly string?[] Symbols = Table(new Dictionary<int, string>
    {
        [1] = "Program::Run",
        [10] = "Program::Run in A.cs:line 11",
        [11] = "Program::Run in A.cs:line 12",
        [12] = "Program::Run in sub/B.cs:line 5",
        [13] = "Program::Run in A.cs:line x",
        [14] = "Other::M in A.cs:line 3",
        [15] = "Program::Run in :line 3",
        [16] = "Program::Run in A.cs:line 0",
        [17] = "Program::Run",
        [18] = "Program::Runner in A.cs:line 4",
        [2] = "Ns.Type::Method",
        [20] = "Ns.Type::Method in A.cs:line 30",
        [64] = "Big::Id",
        [300] = "Big::Id in C.cs:line 1",
        [3] = "Lib::Call",
        [30] = "Lib::Call in https://raw.githubusercontent.com/owner/lib/0123abc/src/Lib.cs:line 7",
        [31] = "Lib::Call in /src/App/Local.cs:line 9",
        [32] = "Lib::Call in C:\\src\\App\\Windows.cs:line 2",
    });

    // A symbol table as the sidecar reader returns it: each text at its ID.
    internal static string?[] Table(IReadOnlyDictionary<int, string> entries)
    {
        var table = new string?[entries.Keys.Max() + 1];
        foreach (var (id, text) in entries) table[id] = text;
        return table;
    }

    [Fact]
    public void RowsFollowTheMarkersWithLineZeroAtJoinsAndHiddenPoints()
    {
        var first = new Body();
        var line11 = first.Marker(1, 10);
        first.Add(Nop);
        first.Add(0x02, 0x40); // block
        var line12 = first.Marker(1, 11);
        first.Add(0x0c, 0x00); // br 0
        var dead = first.Add(Nop);
        first.Add(End);
        first.Add(Nop); // a join after the block, already on line 0
        first.Marker(1, 1); // a hidden sequence point, still line 0
        var line5 = first.Marker(1, 12);
        first.Marker(1, 12);
        first.Marker(1, 1, callee: 1); // the decoy: fewer markers call it
        var second = new Body();
        second.Add(Unreachable);
        second.Add(Nop); // a join before the function's first line has no file to name
        second.Marker(2, 2);
        var line30 = second.Marker(2, 20);
        second.Add(Unreachable);
        var after = second.Add(Nop); // a join after the last marker
        var hidden = new Body();
        hidden.Marker(1, 1);
        hidden.Marker(99, 10); // no such method
        hidden.Marker(-5, 10);
        foreach (var symbol in new[] { 13, 13, 14, 15, 16, 17, 18, 19, 5000 }) hidden.Marker(1, symbol);
        hidden.Marker(2, 10); // another method's line
        hidden.Marker(1, 10, callee: 1);
        hidden.Add(0x41, 0x01, 0x10, 0x00); // one constant only
        var big = new Body();
        var line1 = big.Marker(64, 300);
        byte[][] bodies = [first.Bytes, second.Bytes, hidden.Bytes, big.Bytes];

        var lines = WasmDebugLineScanner.Scan(Module(bodies), Symbols);

        Assert.Equal(["A.cs", "sub/B.cs", "C.cs"], lines.Files);
        Assert.Equal(WasmCustomSections.Code(Module(bodies)).Length, lines.CodeSize);
        Assert.Collection(
            lines.Functions,
            function =>
            {
                Assert.Equal("Program.Run", function.Name);
                Assert.Equal((BodyStart(bodies, 0), BodyStart(bodies, 0) + first.Bytes.Length + 2), (function.Low, function.High));
                Assert.Equal(
                    [
                        new(Address(bodies, 0, line11), 1, 11),
                        new(Address(bodies, 0, line12), 1, 12),
                        new(Address(bodies, 0, dead), 1, 0),
                        new WasmDebugRow(Address(bodies, 0, line5), 2, 5),
                    ],
                    function.Rows);
            },
            function =>
            {
                Assert.Equal("Ns.Type.Method", function.Name);
                Assert.Equal(
                    [new(Address(bodies, 1, line30), 1, 30), new WasmDebugRow(Address(bodies, 1, after), 1, 0)],
                    function.Rows);
            },
            function =>
            {
                Assert.Equal("Big.Id", function.Name);
                Assert.Equal([new WasmDebugRow(Address(bodies, 3, line1), 3, 1)], function.Rows);
            });
        // Every function with a marker is named, past the two imported functions, lines or not.
        Assert.Equal([(2u, "Program.Run"), (3u, "Ns.Type.Method"), (4u, "Program.Run"), (5u, "Big.Id")], lines.Names);
        Assert.Empty(lines.ExistingNames);
    }

    // A Source Link URL is no file a debugger opens, and DWARF would read it as a path under the compilation directory:
    // that code keeps its name but has no lines (line 0 after a local line). Absolute local paths keep theirs.
    [Fact]
    public void ADocumentNamedByAUrlHasNoLines()
    {
        var remoteOnly = new Body();
        remoteOnly.Marker(3, 30);
        var mixed = new Body();
        var local = mixed.Marker(3, 31);
        var remote = mixed.Marker(3, 30);
        var windows = mixed.Marker(3, 32);
        byte[][] bodies = [remoteOnly.Bytes, mixed.Bytes];

        var lines = WasmDebugLineScanner.Scan(Module(bodies), Symbols);

        Assert.Equal(["/src/App/Local.cs", "C:\\src\\App\\Windows.cs"], lines.Files);
        var function = Assert.Single(lines.Functions);
        Assert.Equal(
            [
                new(Address(bodies, 1, local), 1, 9),
                new(Address(bodies, 1, remote), 1, 0),
                new WasmDebugRow(Address(bodies, 1, windows), 2, 2),
            ],
            function.Rows);
        Assert.Equal([(2u, "Lib.Call"), (3u, "Lib.Call")], lines.Names);
    }

    [Fact]
    public void ReadsTheNamesTheModuleAlreadyHas()
    {
        var module = WithNames(Module(Marker(1, 10)), (0, "env.marker"), (2, "kept"));

        Assert.Equal([(0u, "env.marker"), (2u, "kept")], WasmDebugLineScanner.Scan(module, Symbols).ExistingNames);
    }

    // The module with a name section of function names after its other sections.
    internal static byte[] WithNames(byte[] module, params (uint Function, string Name)[] names)
    {
        var entries = new List<byte>(Unsigned((uint)names.Length));
        foreach (var (function, name) in names)
        {
            entries.AddRange(Unsigned(function));
            entries.AddRange(Name(name));
        }
        var output = new List<byte>(module);
        Section(output, 0, [.. Name("name"), 0, 2, .. Name("m"), 1, .. Unsigned((uint)entries.Count), .. entries]);
        return [.. output];
    }

    [Fact]
    public void AModuleWithoutMarkersHasNoFunctions()
    {
        var lines = WasmDebugLineScanner.Scan(Module(Op(Nop), Instructions(Op(0x41, 0x01), Op(0x41, 0x0a))), Symbols);

        Assert.Empty(lines.Files);
        Assert.Empty(lines.Functions);
        Assert.Empty(lines.Names);
        Assert.Empty(WasmDebugLineScanner.Scan(Module(false), Symbols).Functions);
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => WasmDebugLineScanner.Scan(null!, Symbols));
        Assert.Throws<ArgumentNullException>(() => WasmDebugLineScanner.Scan(Module(), null!));
        Assert.Throws<ArgumentNullException>(() => WasmExpressionRemapper.Walk(Module(), 0, 0, null!));
    }

    // A body under construction: each part's offset, and a marker's offset is that of its call.
    private sealed class Body
    {
        private readonly List<byte> _bytes = [];

        public byte[] Bytes => [.. _bytes];

        public int Add(params byte[] bytes)
        {
            var offset = _bytes.Count;
            _bytes.AddRange(bytes);
            return offset;
        }

        public int Marker(int method, int symbol, uint callee = 0)
        {
            var bytes = DebugModuleBuilder.Marker(method, symbol, callee);
            return Add(bytes) + bytes.Length - 2;
        }
    }
}
