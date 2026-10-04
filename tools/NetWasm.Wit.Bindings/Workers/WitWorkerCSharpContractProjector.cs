using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Wit.Bindings.Workers;

public sealed record WitWorkerCSharpContract(
    WitWorkerResolvedContract Contract,
    ImmutableDictionary<int, WitWorkerValueLayout> Layouts,
    WitDocument Document,
    WitWorld World,
    ImmutableDictionary<int, int> TypeIds)
{
    public WitTypeReference Reference(WitWorkerValueReference reference) => reference.Definition is { } id
        ? new WitTypeReference.Defined(TypeIds[id]) : new WitTypeReference.Primitive(reference.Primitive!);
}

public interface IWitWorkerCSharpContractProjector
{
    WitWorkerCSharpContract Project(WitWorkerResolvedContract contract);
}

/// <summary>Adapts the persisted closure to the existing declaration model, without canonical marshalling.</summary>
public sealed class WitWorkerCSharpContractProjector(IWitWorkerValueLayoutPlanner layouts) : IWitWorkerCSharpContractProjector
{
    private readonly IWitWorkerValueLayoutPlanner _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));

    public WitWorkerCSharpContract Project(WitWorkerResolvedContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var layouts = _layouts.Plan(contract);
        if (contract.Exports.Any(export => export!.Kind!.Name != "freestanding")
            || layouts.Values.Any(layout => layout.Kind is "resource" or "handle"))
            throw new NotSupportedException("Resource-bearing application exports are unsupported by Web Workers");
        var ids = contract.Definitions.Keys.Order().Select((id, index) => (id, index))
            .ToImmutableDictionary(item => item.id, item => item.index);
        var types = contract.Definitions.OrderBy(item => item.Key).Select(item =>
        {
            var kind = JsonNode.Parse(item.Value.Kind.GetRawText())!;
            Remap(kind);
            var name = layouts[item.Key].Name;
            return new WitTypeDefinition(ids[item.Key], name,
                JsonSerializer.SerializeToElement(kind), 0);
        }).ToImmutableArray();
        var @interface = new WitInterface(0, "values", "netwasm:worker-client",
            types.ToImmutableDictionary(type => type.Name!, type => type.Id), []);
        var world = new WitWorld(0, "worker", "netwasm:worker-client", [], [new("values", 0, null)]);
        var package = new WitPackage(0, "netwasm:worker-client",
            ImmutableDictionary<string, int>.Empty.Add("values", 0),
            ImmutableDictionary<string, int>.Empty.Add("worker", 0));
        return new(contract, layouts, new([package], [@interface], [world], types, string.Empty), world, ids);

        void Remap(JsonNode node)
        {
            if (node is JsonObject value)
            {
                foreach (var (key, child) in value.ToArray())
                {
                    if (child is JsonValue scalar && scalar.TryGetValue<int>(out var id)) value[key] = ids[id];
                    else if (child is not null) Remap(child);
                }
            }
            else if (node is JsonArray values)
            {
                for (var index = 0; index < values.Count; index++)
                {
                    var child = values[index];
                    if (child is JsonValue scalar && scalar.TryGetValue<int>(out var id)) values[index] = ids[id];
                    else if (child is not null) Remap(child);
                }
            }
        }
    }
}
