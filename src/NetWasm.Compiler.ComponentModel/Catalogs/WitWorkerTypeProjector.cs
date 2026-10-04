using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Compiler.ComponentModel.Catalogs;

public interface IWitWorkerTypeProjector
{
    ImmutableArray<WitWorkerTypeDefinition> Project(
        WitDocument document,
        ImmutableArray<WitWorkerExport> exports);
}

/// <summary>Projects the application type closure without renumbering document-local IDs.</summary>
public sealed class WitWorkerTypeProjector(
    IWitWorkerTypeReferenceReader references,
    IWitTypeIdentityFormatter types,
    IWitInterfaceSpecifierFormatter interfaces) : IWitWorkerTypeProjector
{
    private readonly IWitWorkerTypeReferenceReader _references = references ??
        throw new ArgumentNullException(nameof(references));
    private readonly IWitTypeIdentityFormatter _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly IWitInterfaceSpecifierFormatter _interfaces = interfaces ??
        throw new ArgumentNullException(nameof(interfaces));

    public ImmutableArray<WitWorkerTypeDefinition> Project(
        WitDocument document,
        ImmutableArray<WitWorkerExport> exports)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Types.IsDefault || document.Interfaces.IsDefault || exports.IsDefault)
        {
            throw ComponentException.Invalid("WIT worker type projection requires explicit inventories");
        }
        var visited = new HashSet<int>();
        var path = new HashSet<int>();
        var roots = ImmutableArray.CreateBuilder<WitTypeReference>();
        foreach (var export in exports)
        {
            foreach (var parameter in export.Parameters)
            {
                roots.Add(ReadReference(parameter.Type));
            }
            if (export.Result is { } result)
            {
                roots.Add(ReadReference(result));
            }
            if (export.Kind.ResourceType is { } resource)
            {
                roots.Add(new WitTypeReference.Defined(resource));
            }
        }
        foreach (var root in roots)
        {
            Traverse(document, root, visited, path);
        }
        var resources = new Dictionary<int, bool>();
        foreach (var id in visited)
        {
            var kind = document.Types[id].Kind;
            if (kind.ValueKind == JsonValueKind.Object && kind.TryGetProperty("handle", out var handle)
                && !IsResource(document, handle.EnumerateObject().Single().Value.GetInt32(), resources))
            {
                throw ComponentException.Invalid("WIT worker handle does not reference a resource type");
            }
        }
        var identities = new HashSet<string>(StringComparer.Ordinal);
        return [.. visited.Order().Select(id =>
        {
            var type = document.Types[id];
            var owner = ReadOwner(document, type);
            if (type.Name is not null && (string.IsNullOrWhiteSpace(type.Name)
                || !identities.Add($"{owner!.Identity}#{type.Name}")))
            {
                throw ComponentException.Invalid("WIT worker named type identity is invalid or duplicated");
            }
            return new WitWorkerTypeDefinition(id, type.Name, type.Kind.Clone(), owner);
        })];
    }

    private void Traverse(
        WitDocument document,
        WitTypeReference reference,
        HashSet<int> visited,
        HashSet<int> path)
    {
        if (reference is not WitTypeReference.Defined defined)
        {
            _types.Format(document, reference);
            return;
        }
        var id = defined.Id;
        if ((uint)id >= (uint)document.Types.Length || document.Types[id] is not { } type
            || type.Id != id)
        {
            throw ComponentException.Invalid("WIT worker contract references an unknown type");
        }
        if (!path.Add(id))
        {
            throw ComponentException.Invalid("WIT worker type closure contains a cycle");
        }
        if (visited.Add(id))
        {
            foreach (var child in _references.Read(type.Kind))
            {
                Traverse(document, child, visited, path);
            }
        }
        path.Remove(id);
    }

    private WitWorkerTypeOwner? ReadOwner(WitDocument document, WitTypeDefinition type)
    {
        if (type.OwnerInterface is not { } owner)
        {
            if (type.Name is not null)
            {
                throw ComponentException.Invalid("named WIT worker type requires a supported interface owner");
            }
            return null;
        }
        if ((uint)owner >= (uint)document.Interfaces.Length
            || document.Interfaces[owner] is not { } definition)
        {
            throw ComponentException.Invalid("WIT worker type references an unknown owner");
        }
        return new("interface", _interfaces.Format(definition));
    }

    private static bool IsResource(WitDocument document, int id, Dictionary<int, bool> resolved)
    {
        if (resolved.TryGetValue(id, out var resource))
        {
            return resource;
        }
        var kind = document.Types[id].Kind;
        resource = kind.ValueKind == JsonValueKind.String
            || kind.TryGetProperty("type", out var alias) && alias.ValueKind == JsonValueKind.Number
                && IsResource(document, alias.GetInt32(), resolved);
        resolved.Add(id, resource);
        return resource;
    }

    private static WitTypeReference ReadReference(WitWorkerTypeReference reference) =>
        reference switch
        {
            { Kind: "primitive", Primitive: { } primitive, Definition: null } =>
                new WitTypeReference.Primitive(primitive),
            { Kind: "defined", Primitive: null, Definition: { } id } =>
                new WitTypeReference.Defined(id),
            _ => throw ComponentException.Invalid("WIT worker export type reference is invalid"),
        };
}
