using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Browser.Wit;

internal sealed class VirtualWitDocumentReader : IWitDocumentReader
{
    private readonly ImmutableDictionary<string, string>? _fixedDocuments;
    private readonly ImmutableDictionary<string, string>? _fixedCoreBindings;
    private readonly IBrowserCompilationRequestResolver? _requests;
    private readonly IWitDocumentJsonReader _json;

    internal VirtualWitDocumentReader(
        ImmutableDictionary<string, string> documents,
        IWitDocumentJsonReader json)
        : this(documents, ImmutableDictionary<string, string>.Empty, json)
    {
    }

    internal VirtualWitDocumentReader(
        ImmutableDictionary<string, string> documents,
        ImmutableDictionary<string, string> coreBindings,
        IWitDocumentJsonReader json)
    {
        _fixedDocuments = documents ?? throw new ArgumentNullException(nameof(documents));
        _fixedCoreBindings = coreBindings ??
            throw new ArgumentNullException(nameof(coreBindings));
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

    public WitDocument Read(string path, string? world)
    {
        var document = Read(path);
        var selectedWorld = document.SelectWorld(world);
        var request = _requests?.Resolve();
        var inventories = request?.WitCoreBindingInventories ?? _fixedCoreBindings!;
        if (inventories.TryGetValue(path, out var inventory))
        {
            return WitCoreBindingResolver.Resolve(
                document,
                selectedWorld,
                inventory);
        }
        if (selectedWorld.Imports.Any(item => item.InterfaceId is not null) ||
            selectedWorld.Exports.Any(item => item.InterfaceId is not null))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.ComponentContract,
                "browser compilation requires a core binding inventory for WIT interface placements"));
        }
        return document;
    }
}
