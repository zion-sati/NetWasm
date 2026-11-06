using System.Collections.Generic;
using System;
using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Merging;

internal readonly record struct WasmMergeRange(int Start, int Length);

internal sealed record WasmMergeImport(string Module, string Name, byte Kind, uint Type, WasmMergeRange Descriptor);

internal sealed record WasmMergeExport(string Name, byte Kind, uint Index);

/// <summary>
/// One input of a merge, read once: what the merged module renumbers is parsed, and everything
/// else is kept as offsets into the input's bytes.
/// </summary>
internal sealed class WasmMergeModule
{
    public const byte FunctionKind = 0;
    public const byte TagKind = 4;
    private static readonly string[] KindNames = ["function", "table", "memory", "global", "tag"];

    private readonly WasmInlineAnnotationSelection _inlineAnnotations;
    private bool _sawInlineHints;

    private WasmMergeModule(string name, byte[] bytes, WasmInlineAnnotationSelection inlineAnnotations)
    {
        Name = name;
        Bytes = bytes;
        _inlineAnnotations = inlineAnnotations;
    }

    public string Name { get; }
    public byte[] Bytes { get; }
    public List<WasmMergeRange> Types { get; } = [];
    public List<WasmMergeImport> Imports { get; } = [];
    // Each kind's imports, as positions in Imports: an import's index in its kind's space.
    public List<int>[] ImportsOfKind { get; } = [[], [], [], [], []];
    public List<uint> Functions { get; } = [];
    public List<WasmMergeRange> Tables { get; } = [];
    public List<WasmMergeRange> Memories { get; } = [];
    public List<uint> Tags { get; } = [];
    public List<(WasmMergeRange Type, int Initializer)> Globals { get; } = [];
    public List<WasmMergeExport> Exports { get; } = [];
    public uint? Start { get; private set; }
    public List<int> Elements { get; } = [];
    public List<WasmMergeRange> Bodies { get; } = [];
    public List<int> Data { get; } = [];
    public bool HasDataCount { get; private set; }
    // Function-level inline hints (binaryen.inline) the input's selection keeps, which the merged
    // module carries over.
    public List<(uint Function, WasmMergeRange Hint)> InlineHints { get; } = [];
    // Other custom sections, copied whole. The name section is kept apart: a merge drops it, as
    // wasm-merge does, unless it keeps names, which it then remaps.
    public List<WasmMergeRange> CustomSections { get; } = [];

    // The name section's contents after its name.
    public WasmMergeRange? NameSection { get; private set; }

    public static string KindName(byte kind) => KindNames[kind];

    public int Defined(byte kind) => kind switch
    {
        0 => Functions.Count,
        1 => Tables.Count,
        2 => Memories.Count,
        3 => Globals.Count,
        _ => Tags.Count,
    };

    public static WasmMergeModule Parse(WasmMergeInput input)
    {
        if (!Enum.IsDefined(input.InlineAnnotations))
            throw new ArgumentOutOfRangeException(nameof(input), "The inline hint selection is unknown.");
        var module = new WasmMergeModule(input.Name, input.Module, input.InlineAnnotations);
        ReadOnlySpan<byte> bytes = input.Module;
        if (!bytes.StartsWith(WasmBinaryFormat.Header))
            throw WasmMergeRead.Invalid($"'{input.Name}' is not a WebAssembly core module");
        var at = WasmBinaryFormat.Header.Length;
        while (at < bytes.Length)
        {
            var id = WasmMergeRead.Byte(bytes, ref at);
            var size = WasmMergeRead.Unsigned(bytes, ref at);
            var start = at;
            WasmMergeRead.Bytes(bytes, ref at, size);
            var end = at;
            at = start;
            module.Section(id, bytes, ref at, end);
            if (at != end) throw WasmMergeRead.Invalid($"'{input.Name}' holds section {id} with trailing data");
        }
        if (module.Bodies.Count != module.Functions.Count)
        {
            throw WasmMergeRead.Invalid(
                $"'{input.Name}' declares {module.Functions.Count} functions but holds {module.Bodies.Count} bodies");
        }
        return module;
    }

    private void Section(byte id, ReadOnlySpan<byte> bytes, ref int at, int end)
    {
        switch (id)
        {
            case 0:
                Custom(bytes, ref at, end);
                return;
            case 12:
                WasmMergeRead.Unsigned(bytes, ref at);
                HasDataCount = true;
                return;
            case 8:
                Start = WasmMergeRead.Unsigned(bytes, ref at);
                return;
            case > 13:
                throw WasmMergeRead.Invalid($"'{Name}' holds section {id}, which the merger does not know");
        }
        for (var count = WasmMergeRead.Unsigned(bytes, ref at); count > 0; count--)
            Entry(id, bytes, ref at);
    }

    private void Entry(byte id, ReadOnlySpan<byte> bytes, ref int at)
    {
        var start = at;
        switch (id)
        {
            case 1:
                if (WasmMergeRead.Byte(bytes, ref at) != 0x60)
                    throw WasmMergeRead.Invalid($"'{Name}' declares a type that is not a function type");
                for (var vectors = 0; vectors < 2; vectors++)
                {
                    for (var count = WasmMergeRead.Unsigned(bytes, ref at); count > 0; count--)
                        WasmMergeRead.ValueType(bytes, ref at);
                }
                Types.Add(new(start, at - start));
                break;
            case 2:
                Import(bytes, ref at);
                break;
            case 3:
                Functions.Add(WasmMergeRead.Unsigned(bytes, ref at));
                break;
            case 4:
                TableType(bytes, ref at);
                Tables.Add(new(start, at - start));
                break;
            case 5:
                WasmMergeRead.Limits(bytes, ref at);
                Memories.Add(new(start, at - start));
                break;
            case 6:
                WasmMergeRead.ValueType(bytes, ref at);
                WasmMergeRead.Byte(bytes, ref at);
                var type = new WasmMergeRange(start, at - start);
                Globals.Add((type, at));
                at = WasmExpressionRemapper.Constant(bytes, at, WasmMergeIndices.None, null);
                break;
            case 7:
                var name = WasmMergeRead.Name(bytes, ref at);
                var kind = Kind(bytes, ref at);
                Exports.Add(new(name, kind, WasmMergeRead.Unsigned(bytes, ref at)));
                break;
            case 9:
                Elements.Add(start);
                Element(bytes, ref at);
                break;
            case 10:
                var length = WasmMergeRead.Unsigned(bytes, ref at);
                Bodies.Add(new(at, (int)length));
                WasmMergeRead.Bytes(bytes, ref at, length);
                break;
            case 11:
                Data.Add(start);
                DataSegment(bytes, ref at);
                break;
            default:
                if (WasmMergeRead.Byte(bytes, ref at) != 0)
                    throw WasmMergeRead.Invalid($"'{Name}' declares a tag that is not an exception");
                Tags.Add(WasmMergeRead.Unsigned(bytes, ref at));
                break;
        }
    }

    private void Import(ReadOnlySpan<byte> bytes, ref int at)
    {
        var module = WasmMergeRead.Name(bytes, ref at);
        var name = WasmMergeRead.Name(bytes, ref at);
        var kind = Kind(bytes, ref at);
        var start = at;
        var type = 0u;
        switch (kind)
        {
            case 0:
                type = WasmMergeRead.Unsigned(bytes, ref at);
                break;
            case 1:
                TableType(bytes, ref at);
                break;
            case 2:
                WasmMergeRead.Limits(bytes, ref at);
                break;
            case 3:
                WasmMergeRead.ValueType(bytes, ref at);
                WasmMergeRead.Byte(bytes, ref at);
                break;
            default:
                WasmMergeRead.Byte(bytes, ref at);
                type = WasmMergeRead.Unsigned(bytes, ref at);
                break;
        }
        ImportsOfKind[kind].Add(Imports.Count);
        Imports.Add(new(module, name, kind, type, new(start, at - start)));
    }

    private byte Kind(ReadOnlySpan<byte> bytes, ref int at)
    {
        var kind = WasmMergeRead.Byte(bytes, ref at);
        return kind <= TagKind ? kind : throw WasmMergeRead.Invalid($"'{Name}' names an external of kind {kind}");
    }

    private void TableType(ReadOnlySpan<byte> bytes, ref int at)
    {
        if (WasmMergeRead.Byte(bytes, ref at) == 0x40)
            throw WasmMergeRead.Invalid($"'{Name}' declares a table with an initializer, which the merger does not copy");
        at--;
        WasmMergeRead.ValueType(bytes, ref at);
        WasmMergeRead.Limits(bytes, ref at);
    }

    // An element segment: active ones (bit 0 clear) name a table (bit 1) and an offset;
    // all but the oldest form name an element kind; bit 2 holds expressions, not functions.
    private void Element(ReadOnlySpan<byte> bytes, ref int at)
    {
        var flags = WasmMergeRead.Unsigned(bytes, ref at);
        if (flags > 7) throw WasmMergeRead.Invalid($"'{Name}' holds an element segment of form {flags}");
        if ((flags & 1) == 0)
        {
            if ((flags & 2) != 0) WasmMergeRead.Unsigned(bytes, ref at);
            at = WasmExpressionRemapper.Constant(bytes, at, WasmMergeIndices.None, null);
        }
        if ((flags & 3) != 0) WasmMergeRead.ValueType(bytes, ref at);
        for (var count = WasmMergeRead.Unsigned(bytes, ref at); count > 0; count--)
        {
            if ((flags & 4) != 0) at = WasmExpressionRemapper.Constant(bytes, at, WasmMergeIndices.None, null);
            else WasmMergeRead.Unsigned(bytes, ref at);
        }
    }

    private void DataSegment(ReadOnlySpan<byte> bytes, ref int at)
    {
        var flags = WasmMergeRead.Unsigned(bytes, ref at);
        if (flags > 2) throw WasmMergeRead.Invalid($"'{Name}' holds a data segment of form {flags}");
        if (flags == 2) WasmMergeRead.Unsigned(bytes, ref at);
        if (flags != 1) at = WasmExpressionRemapper.Constant(bytes, at, WasmMergeIndices.None, null);
        WasmMergeRead.Bytes(bytes, ref at, WasmMergeRead.Unsigned(bytes, ref at));
    }

    private void Custom(ReadOnlySpan<byte> bytes, ref int at, int end)
    {
        var start = at;
        var name = WasmMergeRead.Name(bytes, ref at);
        if (name != "binaryen.inline")
        {
            if (name == "name") NameSection = new(at, end - at);
            else CustomSections.Add(new(start, end - start));
            at = end;
            return;
        }
        if (_sawInlineHints) throw WasmMergeRead.Invalid($"'{Name}' holds two inline hint sections");
        _sawInlineHints = true;
        var seen = new HashSet<uint>();
        for (var functions = WasmMergeRead.Unsigned(bytes, ref at); functions > 0; functions--)
        {
            var function = WasmMergeRead.Unsigned(bytes, ref at);
            if (!seen.Add(function)) throw WasmMergeRead.Invalid($"'{Name}' hints function {function} twice");
            for (var hints = WasmMergeRead.Unsigned(bytes, ref at); hints > 0; hints--)
            {
                var offset = WasmMergeRead.Unsigned(bytes, ref at);
                var length = WasmMergeRead.Unsigned(bytes, ref at);
                var hint = new WasmMergeRange(at, (int)length);
                var value = WasmMergeRead.Bytes(bytes, ref at, length);
                // Size-aware selection drops "always inline" (127) hints on whole functions.
                var kept = _inlineAnnotations switch
                {
                    WasmInlineAnnotationSelection.Remove => false,
                    WasmInlineAnnotationSelection.SizeAware => offset != 0 || value is not [127],
                    _ => true,
                };
                if (!kept) continue;
                if (offset != 0)
                {
                    throw WasmMergeRead.Invalid(
                        $"'{Name}' holds an inline hint inside a function body, which the merger does not move");
                }
                InlineHints.Add((function, hint));
            }
        }
    }
}
