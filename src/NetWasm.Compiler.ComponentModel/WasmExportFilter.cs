using System;
using System.Collections.Generic;

namespace NetWasm.Compiler.ComponentModel;

/// <summary>
/// Which exports a core module keeps under an export selection, one export at a time: a
/// component keeps only its prefixed exports, among them exactly one memory; a raw module drops
/// the internal exports the selection names, each of which it must export with that kind.
/// </summary>
public sealed class WasmExportFilter
{
    private readonly string? _prefix;
    private readonly Dictionary<string, byte> _removed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _matched = new(StringComparer.Ordinal);
    private int _memoryExports;

    public WasmExportFilter(WasmExportSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.RemovedExports.IsDefault)
            throw ComponentException.Invalid("core module export selection is uninitialized");
        if (selection.ComponentPrefix is { } prefix)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
            if (!selection.RemovedExports.IsEmpty)
                throw ComponentException.Invalid("core module export selection combines incompatible policies");
        }
        foreach (var export in selection.RemovedExports)
        {
            if (export is null || string.IsNullOrWhiteSpace(export.Name) || export.Kind > 4 ||
                !_removed.TryAdd(export.Name, export.Kind))
                throw ComponentException.Invalid("core module internal export selection is invalid");
        }
        _prefix = selection.ComponentPrefix;
    }

    public bool Keeps(string name, byte kind)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_prefix is not null && string.Equals(name, _prefix + "_memory", StringComparison.Ordinal) &&
            kind == WasmBinaryFormat.MemoryExternalKind)
        {
            _memoryExports++;
        }
        if (_removed.TryGetValue(name, out var expectedKind))
        {
            if (kind != expectedKind || !_matched.Add(name))
                throw ComponentException.Invalid("core module internal export has a conflicting kind or identity");
            return false;
        }
        return _prefix is null || IsComponentExport(name, _prefix);
    }

    // After every export: what the selection requires of the module as a whole.
    public void Complete()
    {
        if (_prefix is not null && _memoryExports != 1)
        {
            throw ComponentException.Invalid(
                $"core module must export exactly one memory named '{_prefix}_memory'");
        }
        if (_matched.Count != _removed.Count)
            throw ComponentException.Invalid("core module is missing a required internal export");
    }

    // wit-component's standard names have no async lift, so a callback-lifted export and its
    // callback keep their full names.
    private static bool IsComponentExport(string name, string prefix) =>
        name.StartsWith(prefix + "_", StringComparison.Ordinal) ||
        name.StartsWith(prefix + "|", StringComparison.Ordinal) ||
        name.StartsWith("[async-lift]", StringComparison.Ordinal) ||
        name.StartsWith("[callback][async-lift]", StringComparison.Ordinal);
}
