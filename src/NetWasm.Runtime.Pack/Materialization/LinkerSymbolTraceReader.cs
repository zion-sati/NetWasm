using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class LinkerSymbolTraceReader : ILinkerSymbolTraceReader
{
    private static readonly (string Marker, RuntimeLinkerSymbolEventKind Kind)[] Markers =
    [
        (": lazy definition of ", RuntimeLinkerSymbolEventKind.LazyDefinition),
        (": definition of ", RuntimeLinkerSymbolEventKind.Definition),
        (": reference to ", RuntimeLinkerSymbolEventKind.Reference),
    ];

    public ImmutableArray<RuntimeLinkerSymbolEvent> Read(string trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (trace.Length == 0)
            return [];
        var events = ImmutableArray.CreateBuilder<RuntimeLinkerSymbolEvent>();
        using var reader = new StringReader(trace);
        while (reader.ReadLine() is { } line)
        {
            var separator = -1;
            string? marker = null;
            var kind = default(RuntimeLinkerSymbolEventKind);
            foreach (var candidate in Markers)
            {
                var candidateSeparator = line.LastIndexOf(candidate.Marker, StringComparison.Ordinal);
                if (candidateSeparator > separator)
                {
                    separator = candidateSeparator;
                    marker = candidate.Marker;
                    kind = candidate.Kind;
                }
            }
            if (separator <= 0 || marker is null)
                throw Invalid();
            var input = line[..separator];
            var entry = line[(separator + marker.Length)..];
            if (entry.Length == 0 || HasLineControl(input) || HasLineControl(entry) ||
                Markers.Any(candidate => entry.Contains(candidate.Marker, StringComparison.Ordinal)))
                throw Invalid();
            events.Add(new(input, kind, entry));
        }
        return events.ToImmutable();
    }

    private static bool HasLineControl(string value) => value.IndexOfAny(['\0', '\r', '\n']) >= 0;

    private static InvalidOperationException Invalid() =>
        new("The native linker symbol trace is missing or invalid.");
}
