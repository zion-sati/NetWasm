using System;
using System.Collections.Immutable;
using System.IO;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Browser.Wit;

internal sealed class VirtualWitDocumentReader(
    ImmutableDictionary<string, string> documents,
    IWitDocumentJsonReader json) : IWitDocumentReader
{
    private readonly ImmutableDictionary<string, string> _documents =
        documents ?? throw new ArgumentNullException(nameof(documents));
    private readonly IWitDocumentJsonReader _json = json ?? throw new ArgumentNullException(nameof(json));

    public WitDocument Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _documents.TryGetValue(path, out var normalized)
            ? _json.Read(normalized)
            : throw new FileNotFoundException("A normalized virtual WIT document was not supplied.", path);
    }
}
