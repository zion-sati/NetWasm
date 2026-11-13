using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Merging;

/// <summary>A core module to merge, under the name the other inputs import its exports by.</summary>
public sealed record WasmMergeInput(string Name, byte[] Module)
{
    // Which of the module's function-level inline hints the merged module keeps.
    public WasmInlineAnnotationSelection InlineAnnotations { get; init; } = WasmInlineAnnotationSelection.Preserve;
}

public interface IWasmCoreModuleMerger
{
    // One module holding every input: each import that names another input's export becomes
    // that export's entity; every other import, and every definition, is kept, and every export
    // the selection keeps (all of them without one). With keepNames, the inputs' function names
    // name the merged functions, as wasm-merge -g keeps them.
    byte[] Merge(IReadOnlyList<WasmMergeInput> inputs, WasmExportSelection? exports = null, bool keepNames = false);
}

/// <summary>
/// Merges NetWasm's core modules as wasm-merge did, without reading them into an IR: the
/// application's code is copied with only its moved indices rewritten, so a merge costs about
/// a copy of the module. Like wasm-merge, it optimizes nothing: identical function types become
/// one, and unused definitions stay for the optimizer to remove at O1 and above.
/// </summary>
public sealed class WasmCoreModuleMerger : IWasmCoreModuleMerger
{
    public byte[] Merge(IReadOnlyList<WasmMergeInput> inputs, WasmExportSelection? exports = null, bool keepNames = false)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0) throw new ArgumentException("A merge needs at least one module.", nameof(inputs));
        var filter = exports is null ? null : new WasmExportFilter(exports);
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        var modules = new WasmMergeModule[inputs.Count];
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index] ?? throw new ArgumentException("A merge input is missing.", nameof(inputs));
            if (!names.TryAdd(input.Name, index))
                throw WasmMergeRead.Invalid($"two modules to merge are both named '{input.Name}'");
            modules[index] = WasmMergeModule.Parse(input);
        }
        return new WasmMergePlan(modules, names, filter, keepNames).Write();
    }
}

/// <summary>Links NetWasm's runtime, application and support modules into one core module.</summary>
public sealed class WasmCoreModuleMergeRunner(
    IByteFileReader reader,
    IByteFileWriter writer,
    IWasmCoreModuleMerger merger) : IComponentCoreModuleMergeRunner
{
    private readonly IByteFileReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private readonly IByteFileWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly IWasmCoreModuleMerger _merger = merger ?? throw new ArgumentNullException(nameof(merger));

    public void Run(ComponentCoreModuleMergeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var inputs = new List<WasmMergeInput>
        {
            new(RuntimeAbi.RuntimeModule, _reader.Read(request.RuntimeModulePath))
            {
                InlineAnnotations = request.RuntimeInlineAnnotations,
            },
            new(RuntimeAbi.ApplicationModule, _reader.Read(request.ApplicationModulePath))
            {
                InlineAnnotations = request.ApplicationInlineAnnotations,
            },
            new("env", _reader.Read(request.EnvironmentModulePath)),
        };
        if (request.HostModulePath is not null)
            inputs.Add(new(RuntimeAbi.HostModule, _reader.Read(request.HostModulePath)));
        if (request.ManagedExecutableAdapterModulePath is not null)
            inputs.Add(new("netwasm.command.v1", _reader.Read(request.ManagedExecutableAdapterModulePath)));
        // One pass selects the hints and the exports as it merges; no intermediate module is written.
        _writer.Write(request.OutputPath, _merger.Merge(inputs, request.Exports, request.KeepNames));
    }
}

/// <summary>
/// Where every input's entities land in the merged module: the imports no input resolves first,
/// by kind, then each input's definitions in input order, with the functions an input hints
/// never to inline after every other function.
/// </summary>
internal sealed class WasmMergePlan
{
    private const int Kinds = 5;
    private readonly WasmMergeModule[] _modules;
    private readonly Dictionary<string, int> _names;
    private readonly List<byte[]> _types = [];
    private readonly Dictionary<string, int> _typeIndices = new(StringComparer.Ordinal);
    private readonly int[][] _typeMaps;
    // The merged imports of each kind, as (input, import), and where each input's import went.
    private readonly List<(int Module, int Import)>[] _imports = [[], [], [], [], []];
    private readonly Dictionary<(byte Kind, string Module, string Name, string Descriptor), int> _importIndices = [];
    private readonly int[][] _mergedImports;
    private readonly int[][] _bases;
    private readonly int[][] _functionSlots;
    private readonly (int Module, int Function)[] _functionOrder;
    private readonly WasmMergeIndices[] _indices;
    private readonly List<int> _starts = [];
    private readonly int _startType = -1;
    private readonly int _functionCount;
    private readonly WasmMergeSink _output;
    private readonly WasmMergeSink _section = new();
    private readonly WasmExportFilter? _exportFilter;
    private readonly bool _keepNames;

    public WasmMergePlan(WasmMergeModule[] modules, Dictionary<string, int> names, WasmExportFilter? exports,
        bool keepNames = false)
    {
        _modules = modules;
        _keepNames = keepNames;
        _names = names;
        _exportFilter = exports;
        _output = new(modules.Sum(module => module.Bytes.Length) + 1024);
        _typeMaps = [.. modules.Select(module => module.Types
            .Select(type => Type(module.Bytes.AsSpan(type.Start, type.Length))).ToArray())];
        _mergedImports = [.. modules.Select((module, index) => module.Imports
            .Select((_, import) => Unresolved(index, import)).ToArray())];
        _bases = new int[modules.Length][];
        var next = _imports.Select(imports => imports.Count).ToArray();
        for (var module = 0; module < modules.Length; module++)
        {
            _bases[module] = new int[Kinds];
            for (byte kind = 0; kind < Kinds; kind++)
            {
                _bases[module][kind] = next[kind];
                next[kind] += modules[module].Defined(kind);
            }
        }
        _functionCount = next[WasmMergeModule.FunctionKind];
        (_functionSlots, _functionOrder) = PlaceFunctions(modules, _imports[WasmMergeModule.FunctionKind].Count);
        var data = 0;
        var elements = 0;
        _indices = new WasmMergeIndices[modules.Length];
        for (var module = 0; module < modules.Length; module++)
        {
            var input = modules[module];
            _indices[module] = new(_typeMaps[module], Map(module, 0), Map(module, 1), Map(module, 2), Map(module, 3),
                Map(module, 4), [.. Enumerable.Range(data, input.Data.Count)],
                [.. Enumerable.Range(elements, input.Elements.Count)]);
            data += input.Data.Count;
            elements += input.Elements.Count;
            if (input.Start is { } start) _starts.Add(WasmExpressionRemapper.Mapped(_indices[module].Functions!, start));
        }
        // More than one start function: one more function calls each, in input order.
        if (_starts.Count > 1) _startType = Type([0x60, 0x00, 0x00]);
    }

    public byte[] Write()
    {
        _output.Write(WasmBinaryFormat.Header);
        Section(1, _types.Count, () =>
        {
            foreach (var type in _types) _section.Write(type);
        });
        Section(2, _imports.Sum(imports => imports.Count), WriteImports);
        var startFunction = _starts.Count > 1;
        Section(3, _functionCount - _imports[0].Count + (startFunction ? 1 : 0), () =>
        {
            foreach (var (module, function) in _functionOrder)
                _section.Unsigned((uint)WasmExpressionRemapper.Mapped(_typeMaps[module], _modules[module].Functions[function]));
            if (startFunction) _section.Unsigned((uint)_startType);
        });
        Section(4, _modules.Sum(module => module.Tables.Count), () => Ranges(module => module.Tables));
        Section(5, _modules.Sum(module => module.Memories.Count), () => Ranges(module => module.Memories));
        Section(13, _modules.Sum(module => module.Tags.Count), () => Each((module, index) =>
        {
            foreach (var type in module.Tags)
            {
                _section.Byte(0);
                _section.Unsigned((uint)WasmExpressionRemapper.Mapped(_typeMaps[index], type));
            }
        }));
        Section(6, _modules.Sum(module => module.Globals.Count), () => Each((module, index) =>
        {
            foreach (var (type, initializer) in module.Globals)
            {
                _section.Write(module.Bytes.AsSpan(type.Start, type.Length));
                WasmExpressionRemapper.Constant(module.Bytes, initializer, _indices[index], _section);
            }
        }));
        var exports = Exports();
        Section(7, exports.Count, () =>
        {
            foreach (var (export, index) in exports) WriteExport(export, index);
        });
        if (_starts.Count > 0)
        {
            _section.Clear();
            _section.Unsigned((uint)(startFunction ? _functionCount : _starts[0]));
            _output.Section(8, _section);
        }
        Section(9, _modules.Sum(module => module.Elements.Count), () => Each((module, index) =>
        {
            foreach (var start in module.Elements) Element(module, _indices[index], start);
        }));
        if (_modules.Any(module => module.HasDataCount))
        {
            _section.Clear();
            _section.Unsigned((uint)_modules.Sum(module => module.Data.Count));
            _output.Section(12, _section);
        }
        WriteInlineHints();
        Section(10, _functionCount - _imports[0].Count + (startFunction ? 1 : 0), () => WriteCode(startFunction));
        Section(11, _modules.Sum(module => module.Data.Count), () => Each((module, index) =>
        {
            foreach (var start in module.Data) DataSegment(module, _indices[index], start);
        }));
        foreach (var module in _modules)
        {
            foreach (var custom in module.CustomSections)
            {
                _output.Byte(0);
                _output.Unsigned((uint)custom.Length);
                _output.Write(module.Bytes.AsSpan(custom.Start, custom.Length));
            }
        }
        if (_keepNames) WriteNames();
        return _output.ToArray();
    }

    // The function names the inputs' name sections give, under their merged indices; an imported
    // function that another input defines takes the name its definition has there.
    private void WriteNames()
    {
        var names = new SortedDictionary<int, string>();
        foreach (var definitions in new[] { true, false })
        {
            for (var index = 0; index < _modules.Length; index++)
            {
                var module = _modules[index];
                if (module.NameSection is not { } range) continue;
                var imported = module.ImportsOfKind[WasmMergeModule.FunctionKind].Count;
                foreach (var (function, name) in FunctionNames(module.Bytes, range))
                {
                    if (function < imported == definitions) continue;
                    names.TryAdd(WasmExpressionRemapper.Mapped(_indices[index].Functions!, function), name);
                }
            }
        }
        if (names.Count == 0) return;
        var functions = new WasmMergeSink();
        functions.Unsigned((uint)names.Count);
        foreach (var (function, name) in names)
        {
            functions.Unsigned((uint)function);
            functions.Name(name);
        }
        _section.Clear();
        _section.Name("name");
        _section.Byte(1);
        _section.Unsigned((uint)functions.Length);
        _section.Write(functions.Written);
        _output.Section(0, _section);
    }

    // The function name subsection's entries; the module and local name subsections are not kept.
    private static List<(uint Function, string Name)> FunctionNames(byte[] bytes, WasmMergeRange range)
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

    private int Type(ReadOnlySpan<byte> type)
    {
        var key = Convert.ToBase64String(type);
        if (!_typeIndices.TryGetValue(key, out var index))
        {
            index = _types.Count;
            _types.Add(type.ToArray());
            _typeIndices.Add(key, index);
        }
        return index;
    }

    // An import no input resolves: one merged import for each module, name and signature. -1
    // when another input provides it.
    private int Unresolved(int module, int import)
    {
        var entry = _modules[module].Imports[import];
        if (_names.ContainsKey(entry.Module)) return -1;
        var descriptor = entry.Kind is WasmMergeModule.FunctionKind or WasmMergeModule.TagKind
            ? WasmExpressionRemapper.Mapped(_typeMaps[module], entry.Type).ToString(CultureInfo.InvariantCulture)
            : Convert.ToBase64String(_modules[module].Bytes.AsSpan(entry.Descriptor.Start, entry.Descriptor.Length));
        var key = (entry.Kind, entry.Module, entry.Name, descriptor);
        if (!_importIndices.TryGetValue(key, out var index))
        {
            index = _imports[entry.Kind].Count;
            _imports[entry.Kind].Add((module, import));
            _importIndices.Add(key, index);
        }
        return index;
    }

    // Where each of an input's entities of a kind lands: its imports where they resolve, then
    // its definitions.
    private int[] Map(int module, byte kind)
    {
        var input = _modules[module];
        var imports = input.ImportsOfKind[kind];
        var map = new int[imports.Count + input.Defined(kind)];
        for (var index = 0; index < imports.Count; index++)
            map[index] = Resolve(module, imports[index], 0);
        for (var index = imports.Count; index < map.Length; index++)
            map[index] = Definition(module, kind, index - imports.Count);
        return map;
    }

    private int Definition(int module, byte kind, int defined) => kind == WasmMergeModule.FunctionKind
        ? _functionSlots[module][defined]
        : _bases[module][kind] + defined;

    // Every input's function definitions in input order, the functions it hints never to inline
    // after all the others. Binaryen merges identical functions before it inlines and keeps the
    // first of them, hints and all: one empty [NoInlining] method ahead of the empty constructors
    // and Dispose methods it duplicated made every call to them a call that could not inline
    // (1,565 calls to nothing in the micro-benchmark runner). Behind them, a group of identical
    // functions keeps an ordinary one whenever it has one, and the never-inline method inlines
    // as the function it duplicates does; a function with code of its own keeps its hint.
    private static (int[][] Slots, (int, int)[] Order) PlaceFunctions(WasmMergeModule[] modules, int imported)
    {
        var slots = modules.Select(module => new int[module.Defined(WasmMergeModule.FunctionKind)]).ToArray();
        var order = new List<(int, int)>(slots.Sum(module => module.Length));
        var neverInlined = modules.Select(NeverInlined).ToArray();
        foreach (var last in new[] { false, true })
        {
            for (var module = 0; module < modules.Length; module++)
            {
                for (var function = 0; function < slots[module].Length; function++)
                {
                    if (neverInlined[module].Contains(function) != last) continue;
                    slots[module][function] = imported + order.Count;
                    order.Add((module, function));
                }
            }
        }
        return (slots, [.. order]);
    }

    // The definitions a kept function-level hint marks never to inline (hint value 0).
    private static HashSet<int> NeverInlined(WasmMergeModule module)
    {
        var imported = module.ImportsOfKind[WasmMergeModule.FunctionKind].Count;
        return [.. module.InlineHints
            .Where(entry => entry.Function >= imported && entry.Hint.Length == 1 && module.Bytes[entry.Hint.Start] == 0)
            .Select(entry => (int)entry.Function - imported)];
    }

    private int Resolve(int module, int import, int depth)
    {
        var merged = _mergedImports[module][import];
        if (merged >= 0) return merged;
        var entry = _modules[module].Imports[import];
        var target = _names[entry.Module];
        var provider = _modules[target];
        var kind = WasmMergeModule.KindName(entry.Kind);
        var export = provider.Exports.FirstOrDefault(candidate => candidate.Name == entry.Name && candidate.Kind == entry.Kind)
            ?? throw WasmMergeRead.Invalid(
                $"'{_modules[module].Name}' imports {kind} '{entry.Name}' from '{entry.Module}', which does not export it");
        var imports = provider.ImportsOfKind[entry.Kind];
        if (export.Index >= imports.Count + provider.Defined(entry.Kind))
            throw WasmMergeRead.Invalid($"'{entry.Module}' exports {kind} '{entry.Name}' as an index it does not define");
        if (entry.Kind is WasmMergeModule.FunctionKind or WasmMergeModule.TagKind &&
            WasmExpressionRemapper.Mapped(_typeMaps[module], entry.Type) != ProvidedType(target, entry.Kind, export.Index))
        {
            throw WasmMergeRead.Invalid(
                $"'{_modules[module].Name}' imports {kind} '{entry.Name}' from '{entry.Module}' with another signature");
        }
        if (export.Index >= imports.Count)
            return Definition(target, entry.Kind, (int)export.Index - imports.Count);
        if (depth == _modules.Length)
            throw WasmMergeRead.Invalid($"'{entry.Module}' re-exports {kind} '{entry.Name}' in a cycle of imports");
        return Resolve(target, imports[(int)export.Index], depth + 1);
    }

    private int ProvidedType(int module, byte kind, uint index)
    {
        var provider = _modules[module];
        var imports = provider.ImportsOfKind[kind];
        var type = index < imports.Count
            ? provider.Imports[imports[(int)index]].Type
            : kind == WasmMergeModule.FunctionKind
                ? provider.Functions[(int)index - imports.Count]
                : provider.Tags[(int)index - imports.Count];
        return WasmExpressionRemapper.Mapped(_typeMaps[module], type);
    }

    private void Section(byte id, int count, Action entries)
    {
        if (count == 0) return;
        _section.Clear();
        _section.Unsigned((uint)count);
        entries();
        _output.Section(id, _section);
    }

    private void Each(Action<WasmMergeModule, int> action)
    {
        for (var index = 0; index < _modules.Length; index++) action(_modules[index], index);
    }

    private void Ranges(Func<WasmMergeModule, List<WasmMergeRange>> ranges) => Each((module, _) =>
    {
        foreach (var range in ranges(module)) _section.Write(module.Bytes.AsSpan(range.Start, range.Length));
    });

    // Imports by kind, so each keeps its index within its kind.
    private void WriteImports()
    {
        foreach (var imports in _imports)
        {
            foreach (var (module, import) in imports)
            {
                var entry = _modules[module].Imports[import];
                _section.Name(entry.Module);
                _section.Name(entry.Name);
                _section.Byte(entry.Kind);
                var descriptor = _modules[module].Bytes.AsSpan(entry.Descriptor.Start, entry.Descriptor.Length);
                if (entry.Kind == WasmMergeModule.TagKind) _section.Byte(descriptor[0]);
                if (entry.Kind is WasmMergeModule.FunctionKind or WasmMergeModule.TagKind)
                    _section.Unsigned((uint)WasmExpressionRemapper.Mapped(_typeMaps[module], entry.Type));
                else
                    _section.Write(descriptor);
            }
        }
    }

    // The exports the selection keeps, in input order, with the input each comes from.
    private List<(WasmMergeExport Export, int Module)> Exports()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<(WasmMergeExport, int)>();
        Each((module, index) =>
        {
            foreach (var export in module.Exports)
            {
                if (!names.Add(export.Name))
                    throw WasmMergeRead.Invalid($"two modules to merge both export '{export.Name}'");
                if (_exportFilter?.Keeps(export.Name, export.Kind) ?? true) kept.Add((export, index));
            }
        });
        _exportFilter?.Complete();
        return kept;
    }

    private void WriteExport(WasmMergeExport export, int module)
    {
        var indices = _indices[module];
        int[][] maps = [indices.Functions!, indices.Tables!, indices.Memories!, indices.Globals!, indices.Tags!];
        _section.Name(export.Name);
        _section.Byte(export.Kind);
        _section.Unsigned((uint)WasmExpressionRemapper.Mapped(maps[export.Kind], export.Index));
    }

    // Function-level inline hints, for the merged functions, in function order.
    private void WriteInlineHints()
    {
        var hints = new SortedDictionary<int, List<(WasmMergeModule Module, WasmMergeRange Hint)>>();
        Each((module, index) =>
        {
            foreach (var (function, hint) in module.InlineHints)
            {
                var merged = WasmExpressionRemapper.Mapped(_indices[index].Functions!, function);
                if (!hints.TryGetValue(merged, out var list)) hints.Add(merged, list = []);
                list.Add((module, hint));
            }
        });
        if (hints.Count == 0) return;
        _section.Clear();
        _section.Name("binaryen.inline");
        _section.Unsigned((uint)hints.Count);
        foreach (var (function, list) in hints)
        {
            _section.Unsigned((uint)function);
            _section.Unsigned((uint)list.Count);
            foreach (var (module, hint) in list)
            {
                _section.Unsigned(0);
                _section.Unsigned((uint)hint.Length);
                _section.Write(module.Bytes.AsSpan(hint.Start, hint.Length));
            }
        }
        _output.Section(0, _section);
    }

    private void WriteCode(bool startFunction)
    {
        var body = new WasmMergeSink(1024);
        foreach (var (module, function) in _functionOrder)
        {
            var range = _modules[module].Bodies[function];
            body.Clear();
            WasmExpressionRemapper.Body(_modules[module].Bytes, range.Start, range.Length, _indices[module], body);
            _section.Unsigned((uint)body.Length);
            _section.Write(body.Written);
        }
        if (!startFunction) return;
        body.Clear();
        body.Byte(0);
        foreach (var start in _starts)
        {
            body.Byte(0x10);
            body.Unsigned((uint)start);
        }
        body.Byte(0x0b);
        _section.Unsigned((uint)body.Length);
        _section.Write(body.Written);
    }

    // An element segment, with its table, offset and functions where they landed. An active
    // segment for table 0, which moved, names its table, as the form with a table index does.
    private void Element(WasmMergeModule module, WasmMergeIndices indices, int start)
    {
        ReadOnlySpan<byte> bytes = module.Bytes;
        var at = start;
        var flags = WasmMergeRead.Unsigned(bytes, ref at);
        var written = flags;
        var table = 0;
        if ((flags & 1) == 0)
        {
            table = WasmExpressionRemapper.Mapped(indices.Tables!, (flags & 2) != 0 ? WasmMergeRead.Unsigned(bytes, ref at) : 0);
            if (table != 0) written |= 2;
        }
        _section.Unsigned(written);
        if ((flags & 1) == 0)
        {
            if ((written & 2) != 0) _section.Unsigned((uint)table);
            at = WasmExpressionRemapper.Constant(bytes, at, indices, _section);
        }
        if ((flags & 3) != 0)
        {
            _section.Byte(WasmMergeRead.Byte(bytes, ref at));
        }
        else if ((written & 3) != 0)
        {
            // The table-index form names its element kind: functions, or references to them.
            _section.Byte((flags & 4) != 0 ? (byte)0x70 : (byte)0x00);
        }
        var count = WasmMergeRead.Unsigned(bytes, ref at);
        _section.Unsigned(count);
        for (; count > 0; count--)
        {
            if ((flags & 4) != 0)
                at = WasmExpressionRemapper.Constant(bytes, at, indices, _section);
            else
                _section.Unsigned((uint)WasmExpressionRemapper.Mapped(indices.Functions!, WasmMergeRead.Unsigned(bytes, ref at)));
        }
    }

    // A data segment, with its memory and offset where they landed. An active segment for memory
    // 0, which moved, names its memory.
    private void DataSegment(WasmMergeModule module, WasmMergeIndices indices, int start)
    {
        ReadOnlySpan<byte> bytes = module.Bytes;
        var at = start;
        var flags = WasmMergeRead.Unsigned(bytes, ref at);
        if (flags == 1)
        {
            _section.Unsigned(1);
        }
        else
        {
            var memory = WasmExpressionRemapper.Mapped(indices.Memories!, flags == 2 ? WasmMergeRead.Unsigned(bytes, ref at) : 0);
            if (flags == 2 || memory != 0)
            {
                _section.Unsigned(2);
                _section.Unsigned((uint)memory);
            }
            else
            {
                _section.Unsigned(0);
            }
            at = WasmExpressionRemapper.Constant(bytes, at, indices, _section);
        }
        var length = WasmMergeRead.Unsigned(bytes, ref at);
        _section.Unsigned(length);
        _section.Write(WasmMergeRead.Bytes(bytes, ref at, length));
    }
}
