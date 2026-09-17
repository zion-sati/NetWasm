using System;
using System.Collections.Immutable;
using System.IO;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Browser.Wit;

internal sealed class VirtualWitDocumentReader : IWitDocumentReader
{
    private readonly ImmutableDictionary<string, string>? _fixedDocuments;
    private readonly IBrowserCompilationRequestResolver? _requests;
    private readonly IWitDocumentJsonReader _json;

    internal VirtualWitDocumentReader(
        ImmutableDictionary<string, string> documents,
        IWitDocumentJsonReader json)
    {
        _fixedDocuments = documents ?? throw new ArgumentNullException(nameof(documents));
        _json = json ?? throw new ArgumentNullException(nameof(json));
    }

    public VirtualWitDocumentReader(
        IBrowserCompilationRequestResolver requests,
        IWitDocumentJsonReader json)
    {
        _requests = requests ?? throw new ArgumentNullException(nameof(requests));
        _json = json ?? throw new ArgumentNullException(nameof(json));
    }

    public WitDocument Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var documents = _requests?.Resolve().NormalizedWitDocuments ?? _fixedDocuments!;
        return documents.TryGetValue(path, out var normalized)
            ? _json.Read(normalized)
            : throw new FileNotFoundException("A normalized virtual WIT document was not supplied.", path);
    }
}
