using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace NetWasm.Hosting.Deployment;

// Persisted artifacts are a separate trust boundary from compiler source documents.

public interface IWitWorkerTypeReferenceReader
{
    ImmutableArray<WitWorkerValueReference> Read(JsonElement kind);
}

public interface IWitWorkerKindReferenceReader
{
    ImmutableArray<WitWorkerValueReference> Read(JsonElement value);
}

public sealed class WitWorkerTypeReferenceReader(
    ImmutableDictionary<string, IWitWorkerKindReferenceReader> readers) :
    IWitWorkerTypeReferenceReader
{
    private readonly ImmutableDictionary<string, IWitWorkerKindReferenceReader> _readers =
        readers ?? throw new ArgumentNullException(nameof(readers));

    public ImmutableArray<WitWorkerValueReference> Read(JsonElement kind)
    {
        string name;
        JsonElement value;
        if (kind.ValueKind == JsonValueKind.String)
        {
            name = kind.GetString()!;
            if (name != "resource")
            {
                throw new System.IO.InvalidDataException($"unsupported WIT worker type '{name}'");
            }
            value = kind;
        }
        else if (kind.ValueKind == JsonValueKind.Object)
        {
            var properties = kind.EnumerateObject().ToArray();
            if (properties.Length != 1)
            {
                throw new System.IO.InvalidDataException("WIT worker type must declare exactly one kind");
            }
            name = properties[0].Name;
            if (name == "resource")
            {
                throw new System.IO.InvalidDataException("WIT worker resource kind must be a string");
            }
            value = properties[0].Value;
        }
        else
        {
            throw new System.IO.InvalidDataException("WIT worker type kind is invalid");
        }
        if (!_readers.TryGetValue(name, out var reader))
        {
            throw new System.IO.InvalidDataException($"unsupported WIT worker type '{name}'");
        }
        return reader.Read(value);
    }
}

public sealed class WitWorkerUnaryReferenceReader : IWitWorkerKindReferenceReader
{
    public ImmutableArray<WitWorkerValueReference> Read(JsonElement value) => [
        value.ValueKind switch
        {
            JsonValueKind.String => new WitWorkerValueReference("primitive", value.GetString()!, null),
            JsonValueKind.Number when value.TryGetInt32(out var id) => new WitWorkerValueReference("defined", null, id),
            _ => throw new System.IO.InvalidDataException("WIT worker type reference is invalid"),
        },
    ];
}

public sealed class WitWorkerNamedCollectionReferenceReader(
    string collection,
    bool typed,
    bool nullable,
    int minimumCount,
    int? maximumCount,
    IWitWorkerKindReferenceReader references) : IWitWorkerKindReferenceReader
{
    private readonly IWitWorkerKindReferenceReader _references = references ??
        throw new ArgumentNullException(nameof(references));

    public ImmutableArray<WitWorkerValueReference> Read(JsonElement value)
    {
        RequireMembers(value, [collection]);
        var items = value.GetProperty(collection);
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() < minimumCount
            || maximumCount is { } maximum && items.GetArrayLength() > maximum)
        {
            throw new System.IO.InvalidDataException("WIT worker named type collection is invalid");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<WitWorkerValueReference>();
        foreach (var item in items.EnumerateArray())
        {
            RequireMembers(item, typed ? ["name", "type"] : ["name"]);
            var name = item.GetProperty("name");
            if (name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString())
                || !names.Add(name.GetString()!))
            {
                throw new System.IO.InvalidDataException("WIT worker type member name is invalid or duplicated");
            }
            if (typed)
            {
                var reference = item.GetProperty("type");
                if (!nullable || reference.ValueKind != JsonValueKind.Null)
                {
                    result.AddRange(_references.Read(reference));
                }
            }
        }
        return result.ToImmutable();
    }

    private static void RequireMembers(JsonElement value, ImmutableArray<string> names)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new System.IO.InvalidDataException("WIT worker type member shape is invalid");
        }
        var properties = value.EnumerateObject().ToArray();
        if (properties.Length != names.Length
            || names.Any(name => properties.Count(property => property.Name == name) != 1))
        {
            throw new System.IO.InvalidDataException("WIT worker type member shape is invalid");
        }
    }
}

public sealed class WitWorkerTupleReferenceReader(
    IWitWorkerKindReferenceReader references) : IWitWorkerKindReferenceReader
{
    private readonly IWitWorkerKindReferenceReader _references = references ??
        throw new ArgumentNullException(nameof(references));

    public ImmutableArray<WitWorkerValueReference> Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1
            || !value.TryGetProperty("types", out var types) || types.ValueKind != JsonValueKind.Array)
        {
            throw new System.IO.InvalidDataException("WIT worker tuple shape is invalid");
        }
        return [.. types.EnumerateArray().SelectMany(type => _references.Read(type))];
    }
}

public sealed class WitWorkerResultReferenceReader(
    IWitWorkerKindReferenceReader references) : IWitWorkerKindReferenceReader
{
    private readonly IWitWorkerKindReferenceReader _references = references ??
        throw new ArgumentNullException(nameof(references));

    public ImmutableArray<WitWorkerValueReference> Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 2
            || !value.TryGetProperty("ok", out var ok) || !value.TryGetProperty("err", out var error))
        {
            throw new System.IO.InvalidDataException("WIT worker result shape is invalid");
        }
        var result = ImmutableArray.CreateBuilder<WitWorkerValueReference>();
        foreach (var arm in new[] { ok, error })
        {
            if (arm.ValueKind != JsonValueKind.Null)
            {
                result.AddRange(_references.Read(arm));
            }
        }
        return result.ToImmutable();
    }
}

public sealed class WitWorkerHandleReferenceReader : IWitWorkerKindReferenceReader
{
    public ImmutableArray<WitWorkerValueReference> Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new System.IO.InvalidDataException("WIT worker handle shape is invalid");
        }
        var properties = value.EnumerateObject().ToArray();
        if (properties.Length != 1 || properties[0].Name is not ("own" or "borrow")
            || properties[0].Value.ValueKind != JsonValueKind.Number
            || !properties[0].Value.TryGetInt32(out var id))
        {
            throw new System.IO.InvalidDataException("WIT worker handle shape is invalid");
        }
        return [new WitWorkerValueReference("defined", null, id)];
    }
}

public sealed class WitWorkerResourceReferenceReader : IWitWorkerKindReferenceReader
{
    public ImmutableArray<WitWorkerValueReference> Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() != "resource")
        {
            throw new System.IO.InvalidDataException("WIT worker resource shape is invalid");
        }
        return [];
    }
}

/// <summary>Composition root for the supported normalized WIT reference layouts.</summary>
public static class WitWorkerReferenceComposition
{
    public static ImmutableDictionary<string, IWitWorkerKindReferenceReader> CreateReaders()
    {
        var unary = new WitWorkerUnaryReferenceReader();
        return ImmutableDictionary.CreateRange(StringComparer.Ordinal,
            new (string Key, IWitWorkerKindReferenceReader Value)[]
            {
                ("type", unary), ("list", unary), ("option", unary),
                ("record", new WitWorkerNamedCollectionReferenceReader("fields", true, false, 0, null, unary)),
                ("variant", new WitWorkerNamedCollectionReferenceReader("cases", true, true, 1, null, unary)),
                ("enum", new WitWorkerNamedCollectionReferenceReader("cases", false, false, 1, null, unary)),
                ("flags", new WitWorkerNamedCollectionReferenceReader("flags", false, false, 0, 32, unary)),
                ("tuple", new WitWorkerTupleReferenceReader(unary)),
                ("result", new WitWorkerResultReferenceReader(unary)),
                ("handle", new WitWorkerHandleReferenceReader()),
                ("resource", new WitWorkerResourceReferenceReader()),
            }.Select(entry => new KeyValuePair<string, IWitWorkerKindReferenceReader>(entry.Key, entry.Value)));
    }
}
