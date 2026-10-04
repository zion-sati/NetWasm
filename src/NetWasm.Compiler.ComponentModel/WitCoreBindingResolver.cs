using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel;

public interface IWitCoreBindingResolver
{
    WitDocument Resolve(string witPath, WitDocument document, WitWorld world);
}

public sealed class WitCoreBindingResolver(
    IWasmTools tools,
    IWitWorldSpecifierFormatter worldSpecifiers) : IWitCoreBindingResolver
{
    private const string Prefix = "cm32p2";

    private readonly IWasmTools _tools = tools ??
        throw new ArgumentNullException(nameof(tools));
    private readonly IWitWorldSpecifierFormatter _worldSpecifiers = worldSpecifiers ??
        throw new ArgumentNullException(nameof(worldSpecifiers));

    public WitDocument Resolve(string witPath, WitDocument document, WitWorld world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(witPath);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);

        var result = _tools.Run(
            "component",
            "embed",
            witPath,
            "--world",
            _worldSpecifiers.Format(world),
            "--dummy",
            "-t");
        if (result.ExitCode != 0)
        {
            throw ComponentException.Invalid(
                $"invalid WIT core binding inventory: {NormalizeError(result.StandardError)}");
        }

        return Resolve(document, world, result.StandardOutput);
    }

    public static WitDocument Resolve(
        WitDocument document,
        WitWorld world,
        string coreBindingInventory)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        var inventory = ReadInventory(coreBindingInventory);
        var resolved = world with
        {
            Imports = ResolveItems(
                document, world.Imports, inventory.Imports, "import"),
            Exports = ResolveItems(
                document, world.Exports, inventory.Exports, "export"),
        };
        var index = document.Worlds.IndexOf(world);
        if (index < 0)
        {
            throw ComponentException.Invalid("selected WIT world does not belong to its document");
        }
        return document with { Worlds = document.Worlds.SetItem(index, resolved) };
    }

    private static ImmutableArray<WitWorldItem> ResolveItems(
        WitDocument document,
        ImmutableArray<WitWorldItem> items,
        ImmutableDictionary<string, ImmutableHashSet<string>> namespaces,
        string direction)
    {
        foreach (var duplicate in items
                     .Where(item => item.InterfaceId is not null)
                     .GroupBy(item => item.InterfaceId!.Value)
                     .Where(group => group.Count() > 1))
        {
            var definition = document.Interfaces[duplicate.Key];
            throw ComponentException.Invalid(
                $"WIT {direction} interface '{definition.Package}/{definition.Name}' " +
                "has multiple world placements; managed instance selection is ambiguous");
        }

        return
        [
            .. items.Select(item => ResolveItem(document, item, namespaces, direction)),
        ];
    }

    private static WitWorldItem ResolveItem(
        WitDocument document,
        WitWorldItem item,
        ImmutableDictionary<string, ImmutableHashSet<string>> namespaces,
        string direction)
    {
        if (item.InterfaceId is not { } interfaceId)
        {
            return item with { CoreBindingName = string.Empty };
        }

        var definition = document.Interfaces[interfaceId];
        var semanticName = $"{definition.Package}/{definition.Name}";
        var qualifiedName = CanonicalAbiNames.CoreInterfaceName(semanticName);
        var expectedMembers = ExpectedMembers(document, definition, direction);
        if (expectedMembers.Count == 0)
        {
            return item with { CoreBindingName = qualifiedName };
        }

        var matches = new[] { item.Name, qualifiedName }
            .Distinct(StringComparer.Ordinal)
            .Where(candidate => namespaces.TryGetValue(candidate, out var members) &&
                members.SetEquals(expectedMembers))
            .ToArray();
        if (matches.Length == 0)
        {
            throw ComponentException.Invalid(
                $"WIT {direction} interface '{semanticName}' has no exact core binding namespace and member inventory");
        }
        if (matches.Length != 1)
        {
            throw ComponentException.Invalid(
                $"WIT {direction} interface '{semanticName}' has ambiguous core binding namespaces");
        }
        return item with { CoreBindingName = matches[0] };
    }

    private static ImmutableHashSet<string> ExpectedMembers(
        WitDocument document,
        WitInterface definition,
        string direction)
    {
        var members = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var function in definition.Functions)
        {
            members.Add(function.Name);
            if (direction == "export")
            {
                members.Add(function.Name + "_post");
            }
        }
        foreach (var typeId in definition.Types.Values)
        {
            var type = document.Types[typeId];
            if (type.Kind.ValueKind != JsonValueKind.String ||
                type.Kind.GetString() != "resource")
            {
                continue;
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(type.Name);
            if (direction == "import")
            {
                members.Add(type.Name + "_drop");
            }
            else
            {
                members.Add(type.Name + "_new");
                members.Add(type.Name + "_rep");
                members.Add(type.Name + "_drop");
                members.Add(type.Name + "_dtor");
            }
        }
        return members.ToImmutable();
    }

    private static CoreBindingInventory ReadInventory(string wat)
    {
        if (string.IsNullOrWhiteSpace(wat))
        {
            throw ComponentException.Tool(
                "wasm-tools returned an empty WIT core binding inventory");
        }

        var imports = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var exports = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var line in wat.Split('\n'))
        {
            var value = line.TrimStart();
            if (value.StartsWith("(import \"", StringComparison.Ordinal))
            {
                var module = ReadFirstString(value, "(import \"");
                var name = ReadNextString(value, module);
                if (TryReadImportNamespace(
                        module,
                        out var importNamespace,
                        out var exportedResource))
                {
                    AddMember(
                        exportedResource ? exports : imports,
                        importNamespace,
                        name);
                }
                continue;
            }
            if (value.StartsWith("(export \"", StringComparison.Ordinal))
            {
                var name = ReadFirstString(value, "(export \"");
                if (TryReadExportNamespace(
                        name,
                        out var exportNamespace,
                        out var exportMember))
                {
                    AddMember(exports, exportNamespace, exportMember);
                }
            }
        }
        return new(ToImmutable(imports), ToImmutable(exports));
    }

    private static void AddMember(
        Dictionary<string, HashSet<string>> inventory,
        string coreNamespace,
        string member)
    {
        if (!inventory.TryGetValue(coreNamespace, out var members))
        {
            members = new(StringComparer.Ordinal);
            inventory.Add(coreNamespace, members);
        }
        members.Add(member);
    }

    private static ImmutableDictionary<string, ImmutableHashSet<string>> ToImmutable(
        Dictionary<string, HashSet<string>> inventory) =>
        inventory.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value.ToImmutableHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

    private static string ReadFirstString(string line, string prefix)
    {
        var end = line.IndexOf('"', prefix.Length);
        if (end < 0)
        {
            throw ComponentException.Tool(
                "wasm-tools returned an invalid WIT core binding inventory");
        }
        return line[prefix.Length..end];
    }

    private static string ReadNextString(string line, string firstValue)
    {
        var firstEnd = "(import \"".Length + firstValue.Length;
        var start = line.IndexOf('"', firstEnd + 1);
        var end = start < 0 ? -1 : line.IndexOf('"', start + 1);
        if (start < 0 || end < 0)
        {
            throw ComponentException.Tool(
                "wasm-tools returned an invalid WIT core binding inventory");
        }
        return line[(start + 1)..end];
    }

    private static bool TryReadImportNamespace(
        string module,
        out string value,
        out bool exportedResource)
    {
        exportedResource = false;
        if (module == Prefix)
        {
            value = string.Empty;
            return true;
        }
        var prefix = Prefix + "|";
        if (!module.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = string.Empty;
            return false;
        }
        value = module[prefix.Length..];
        const string exportedResourcePrefix = "_ex_";
        if (value.StartsWith(exportedResourcePrefix, StringComparison.Ordinal))
        {
            value = value[exportedResourcePrefix.Length..];
            exportedResource = true;
        }
        return value.Length > 0;
    }

    private static bool TryReadExportNamespace(
        string name,
        out string value,
        out string member)
    {
        var prefix = Prefix + "|";
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = string.Empty;
            member = string.Empty;
            return false;
        }
        var separator = name.IndexOf('|', prefix.Length);
        if (separator < 0)
        {
            value = string.Empty;
            member = string.Empty;
            return false;
        }
        value = name[prefix.Length..separator];
        member = name[(separator + 1)..];
        return value.Length > 0 && member.Length > 0;
    }

    private static string NormalizeError(string error)
    {
        var normalized = error.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length == 0
            ? "wasm-tools reported an unknown error"
            : normalized;
    }

    private sealed record CoreBindingInventory(
        ImmutableDictionary<string, ImmutableHashSet<string>> Imports,
        ImmutableDictionary<string, ImmutableHashSet<string>> Exports);
}
