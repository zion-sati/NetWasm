using System.Text.Json;
using System.Text.Json.Nodes;

using NuGet.Frameworks;

namespace NetWasm.Sdk.Pack.Restore;

public sealed class RestoreGraphCanonicalizer : IRestoreGraphCanonicalizer
{
    public byte[] Canonicalize(byte[] graph, IReadOnlyDictionary<string, string> profiles)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(profiles);
        var document = JsonNode.Parse(graph) as JsonObject
            ?? throw new JsonException("The restore graph must be an object.");
        var projects = document["projects"] as JsonObject
            ?? throw new JsonException("The restore graph must contain projects.");

        foreach (var project in projects)
        {
            var specification = project.Value as JsonObject
                ?? throw new JsonException("A restore project must be an object.");
            var metadata = specification["restore"] as JsonObject
                ?? throw new JsonException("A restore project must contain metadata.");
            CanonicalizeFrameworks(specification, profiles);
            CanonicalizeFrameworks(metadata, profiles);
        }

        return JsonSerializer.SerializeToUtf8Bytes(document);
    }

    private static void CanonicalizeFrameworks(
        JsonObject specification,
        IReadOnlyDictionary<string, string> profiles)
    {
        if (specification["frameworks"] is null)
        {
            return;
        }

        var frameworks = specification["frameworks"] as JsonObject
            ?? throw new JsonException("Restore frameworks must be an object.");

        foreach (var framework in frameworks)
        {
            if (!profiles.TryGetValue(framework.Key, out var canonical))
            {
                continue;
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(canonical);
            var registered = NuGetFramework.Parse(canonical);
            if (!registered.IsSpecificFramework)
            {
                throw new ArgumentException("A registered restore profile must have a canonical framework identity.", nameof(profiles));
            }
            var information = framework.Value as JsonObject
                ?? throw new JsonException("A registered framework must be an object.");
            var identity = information["framework"]?.GetValue<string>();
            if (identity is not null && identity != framework.Key && identity != canonical && identity != registered.GetShortFolderName())
            {
                throw new JsonException("A registered restore framework has a conflicting identity.");
            }

            // NuGet parses this standard field independently of targetAlias.
            // Keep project aliases and all compatibility/dependency data intact.
            information["framework"] = canonical;
        }
    }
}
