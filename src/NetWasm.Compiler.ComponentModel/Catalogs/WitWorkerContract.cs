using System;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace NetWasm.Compiler.ComponentModel.Catalogs;

public sealed record WitWorkerContract(
    int SchemaVersion,
    string World,
    ImmutableArray<WitWorkerExport> Exports,
    ImmutableArray<WitWorkerTypeDefinition> Types,
    WitWorkerReactor? Reactor);

public sealed record WitWorkerReactor(
    string Interface,
    string JavaScriptRoot,
    string JavaScriptMember);

public sealed record WitWorkerExport(
    string Operation,
    string Placement,
    string WorldItem,
    string? Interface,
    string JavaScriptRoot,
    string JavaScriptMember,
    string Function,
    ImmutableArray<WitWorkerParameter> Parameters,
    WitWorkerTypeReference? Result,
    WitFunctionKind Kind);

public sealed record WitWorkerParameter(
    string Name,
    WitWorkerTypeReference Type);

public sealed record WitWorkerTypeReference(
    string Kind,
    string? Primitive,
    int? Definition);

public sealed record WitWorkerTypeDefinition(
    int Id,
    string? Name,
    JsonElement Kind,
    WitWorkerTypeOwner? Owner);

public sealed record WitWorkerTypeOwner(string Kind, string Identity);

public interface IWitJavaScriptNameFormatter
{
    string FormatMember(string witName);
}

public sealed class WitJavaScriptNameFormatter : IWitJavaScriptNameFormatter
{
    public string FormatMember(string witName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(witName);
        var result = new StringBuilder(witName.Length);
        var upper = false;
        foreach (var character in witName)
        {
            if (character == '-')
            {
                upper = true;
                continue;
            }
            // Match Jco's lower-camel conversion for uniformly cased WIT segments.
            result.Append(upper ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character));
            upper = false;
        }
        if (result.Length == 0 || upper)
        {
            throw ComponentException.Invalid(
                "WIT worker JavaScript member name is invalid");
        }
        return result.ToString();
    }
}
