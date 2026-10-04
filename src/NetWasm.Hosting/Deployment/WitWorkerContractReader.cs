using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Hosting.Deployment;

/// <summary>Validates the persisted application contract once at the host build boundary.</summary>
public sealed class WitWorkerContractReader(IWitWorkerTypeReferenceReader references) : IWitWorkerContractReader
{
    private readonly IWitWorkerTypeReferenceReader _references = references ??
        throw new ArgumentNullException(nameof(references));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly ImmutableHashSet<string> Primitives = ImmutableHashSet.Create(
        StringComparer.Ordinal, "bool", "s8", "u8", "s16", "u16", "s32", "u32",
        "s64", "u64", "f32", "f64", "char", "string");
    private static readonly ImmutableHashSet<string> Reserved = ImmutableHashSet.Create(
        StringComparer.Ordinal, "__proto__", "constructor", "prototype", "dispose", "terminate", "then");

    public WitWorkerResolvedContract Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty) throw Invalid("A build-bound WIT worker contract is required");
        WitWorkerContractArtifact contract;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            RejectDuplicateKeys(document.RootElement);
            contract = document.RootElement.Deserialize<WitWorkerContractArtifact>(JsonOptions)
                ?? throw Invalid("WIT worker contract is empty");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("WIT worker contract is invalid", exception);
        }
        if (contract.SchemaVersion != 2 || string.IsNullOrWhiteSpace(contract.World)
            || contract.Exports.IsDefaultOrEmpty || contract.Types.IsDefault)
            throw Invalid("WIT worker contract shape is invalid");

        var definitions = ImmutableDictionary.CreateBuilder<int, WitWorkerTypeArtifact>();
        var references = ImmutableDictionary.CreateBuilder<int, ImmutableArray<WitWorkerValueReference>>();
        var names = new HashSet<(string Owner, string Name)>();
        foreach (var type in contract.Types)
        {
            if (type is null || type.Id < 0 || !definitions.TryAdd(type.Id, type)
                || type.Owner is { } owner && (owner.Kind != "interface" || string.IsNullOrWhiteSpace(owner.Identity))
                || type.Name is { } name && (string.IsNullOrWhiteSpace(name) || type.Owner is null
                    || !names.Add((type.Owner.Identity, name))))
                throw Invalid("WIT worker named type identity is invalid or duplicated");
            references.Add(type.Id, _references.Read(type.Kind));
        }
        var visited = new HashSet<int>();
        var path = new HashSet<int>();
        var resources = new Dictionary<int, bool>();
        foreach (var id in definitions.Keys) Visit(new("defined", null, id));
        foreach (var type in definitions.Values)
        {
            if (type.Kind.ValueKind == JsonValueKind.Object && type.Kind.TryGetProperty("handle", out _)
                && !IsResource(references[type.Id][0]))
                throw Invalid("WIT worker handle does not reference a resource");
        }
        string? previous = null;
        foreach (var export in contract.Exports)
        {
            if (export is null || string.IsNullOrWhiteSpace(export.Operation)
                || Reserved.Contains(export.Operation)
                || previous is not null && StringComparer.Ordinal.Compare(previous, export.Operation) >= 0
                || string.IsNullOrWhiteSpace(export.WorldItem) || string.IsNullOrWhiteSpace(export.Function)
                || string.IsNullOrWhiteSpace(export.JavaScriptMember) || export.Parameters.IsDefault
                || export.Kind is null)
                throw Invalid("WIT worker export shape is invalid");
            if (export.Placement == "root")
            {
                if (export.Interface is not null || export.JavaScriptRoot is null || export.JavaScriptRoot.Length != 0)
                    throw Invalid("WIT worker root export shape is invalid");
            }
            else if (export.Placement == "interface")
            {
                if (string.IsNullOrWhiteSpace(export.Interface) || string.IsNullOrWhiteSpace(export.JavaScriptRoot))
                    throw Invalid("WIT worker interface export shape is invalid");
            }
            else throw Invalid("WIT worker export placement is invalid");
            var parameters = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in export.Parameters)
            {
                if (parameter is null || string.IsNullOrWhiteSpace(parameter.Name) || !parameters.Add(parameter.Name))
                    throw Invalid("WIT worker export parameter shape is invalid");
                Visit(parameter.Type);
            }
            if (export.Result is not null) Visit(export.Result);
            if (export.Kind.Name == "freestanding")
            {
                if (export.Kind.ResourceType is not null) throw Invalid("WIT worker function kind is invalid");
            }
            else if (export.Kind.Name is "constructor" or "method" or "static"
                && export.Kind.ResourceType is { } resource && IsResource(new("defined", null, resource)))
            {
                Visit(new("defined", null, resource));
            }
            else throw Invalid("WIT worker function kind is invalid");
            previous = export.Operation;
        }
        if (contract.Reactor is { } reactor && (reactor.Interface != "netwasm:runtime/reactor-guest@1.0.0"
            || string.IsNullOrWhiteSpace(reactor.JavaScriptRoot) || string.IsNullOrWhiteSpace(reactor.JavaScriptMember)))
            throw Invalid("WIT worker reactor export shape is invalid");
        return new(contract, definitions.ToImmutable(), references.ToImmutable());

        void Visit(WitWorkerValueReference? reference)
        {
            if (reference is null) throw Invalid("WIT worker type reference is invalid");
            if (reference.Kind == "primitive")
            {
                if (reference.Definition is not null || reference.Primitive is null || !Primitives.Contains(reference.Primitive))
                    throw Invalid("WIT worker primitive reference is invalid");
                return;
            }
            if (reference.Kind != "defined" || reference.Primitive is not null
                || reference.Definition is not { } id || !definitions.ContainsKey(id))
                throw Invalid("WIT worker contract references an unknown type");
            if (!path.Add(id)) throw Invalid("WIT worker type closure contains a cycle");
            if (visited.Add(id)) foreach (var child in references[id]) Visit(child);
            path.Remove(id);
        }

        bool IsResource(WitWorkerValueReference reference)
        {
            if (reference.Definition is not { } id || !definitions.TryGetValue(id, out var type)) return false;
            if (resources.TryGetValue(id, out var known)) return known;
            var result = type.Kind.ValueKind == JsonValueKind.String
                || type.Kind.TryGetProperty("type", out _) && IsResource(references[id][0]);
            resources.Add(id, result);
            return result;
        }
    }

    private static void RejectDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid("WIT worker contract contains duplicate properties");
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var element in value.EnumerateArray()) RejectDuplicateKeys(element);
    }

    private static InvalidDataException Invalid(string message) => new(message);
}

/// <summary>Composition of persisted-contract shape readers, without a compiler dependency.</summary>
public static class WitWorkerContractComposition
{
    public static IWitWorkerContractReader CreateReader() =>
        new WitWorkerContractReader(new WitWorkerTypeReferenceReader(WitWorkerReferenceComposition.CreateReaders()));
}
