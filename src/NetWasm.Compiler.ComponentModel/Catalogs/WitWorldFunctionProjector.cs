using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Compiler.ComponentModel.Catalogs;

public sealed record WitWorldFunctionProjection(
    ImmutableArray<WitInterfaceFunction> Imports,
    ImmutableArray<WitInterfaceFunction> Exports,
    ImmutableArray<string> ImportModules);

public interface IWitWorldFunctionProjector
{
    WitWorldFunctionProjection Project(WitDocument document, WitWorld world);
}

public sealed class WitWorldFunctionProjector(
    IWitWorldValidator worlds,
    IWitWorldSpecifierFormatter worldSpecifiers,
    IWitInterfaceSpecifierFormatter interfaceSpecifiers,
    IWitTypeIdentityFormatter types) : IWitWorldFunctionProjector
{
    private readonly IWitWorldValidator _worlds = worlds ??
        throw new ArgumentNullException(nameof(worlds));
    private readonly IWitWorldSpecifierFormatter _worldSpecifiers = worldSpecifiers ??
        throw new ArgumentNullException(nameof(worldSpecifiers));
    private readonly IWitInterfaceSpecifierFormatter _interfaceSpecifiers = interfaceSpecifiers ??
        throw new ArgumentNullException(nameof(interfaceSpecifiers));
    private readonly IWitTypeIdentityFormatter _types = types ??
        throw new ArgumentNullException(nameof(types));

    public WitWorldFunctionProjection Project(WitDocument document, WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        _worlds.Validate(document, world);
        return new(
            ProjectItems(document, world, world.Imports),
            ProjectItems(document, world, world.Exports),
            ProjectModules(document, world, world.Imports));
    }

    private ImmutableArray<string> ProjectModules(
        WitDocument document,
        WitWorld world,
        ImmutableArray<WitWorldItem> items)
    {
        return [.. items.Select(item =>
            {
                if (item.Function is not null)
                {
                    return _worldSpecifiers.Format(world);
                }
                return _interfaceSpecifiers.Format(document.Interfaces[item.InterfaceId!.Value]);
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private ImmutableArray<WitInterfaceFunction> ProjectItems(
        WitDocument document,
        WitWorld world,
        ImmutableArray<WitWorldItem> items)
    {
        if (items.IsDefault)
        {
            throw ComponentException.Invalid(
                "WIT deployment function inventories must be explicit");
        }

        var functions = ImmutableArray.CreateBuilder<WitInterfaceFunction>();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.Function is { } worldFunction)
            {
                functions.Add(ProjectFunction(
                    document,
                    _worldSpecifiers.Format(world),
                    item.Name,
                    worldFunction));
                continue;
            }

            if (item.InterfaceId is not { } id
                || (uint)id >= (uint)document.Interfaces.Length)
            {
                throw ComponentException.Invalid(
                    "WIT deployment function references an invalid interface");
            }
            var definition = document.Interfaces[id] ?? throw ComponentException.Invalid(
                "WIT deployment function references a missing interface");
            if (definition.Functions.IsDefault)
            {
                throw ComponentException.Invalid(
                    "WIT deployment interface functions must be explicit");
            }
            var specifier = _interfaceSpecifiers.Format(definition);
            functions.AddRange(definition.Functions.Select(function =>
            {
                ArgumentNullException.ThrowIfNull(function);
                return ProjectFunction(document, specifier, function.Name, function);
            }));
        }

        var result = functions
            .OrderBy(function => function.Interface, StringComparer.Ordinal)
            .ThenBy(function => function.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        if (result.Select(function => string.Concat(
                function.Interface,
                "\0",
                function.Name))
            .Distinct(StringComparer.Ordinal)
            .Count() != result.Length)
        {
            throw ComponentException.Invalid(
                "WIT deployment functions contain a duplicate identity");
        }
        return result;
    }

    private WitInterfaceFunction ProjectFunction(
        WitDocument document,
        string interfaceName,
        string name,
        WitFunction function)
    {
        if (string.IsNullOrWhiteSpace(name) || function.Parameters.IsDefault)
        {
            throw ComponentException.Invalid(
                "WIT deployment function is incomplete");
        }
        return new(
            interfaceName,
            name,
            [.. function.Parameters.Select(parameter =>
            {
                ArgumentNullException.ThrowIfNull(parameter);
                return _types.Format(document, parameter.Type);
            })],
            function.Result is null ? [] : [_types.Format(document, function.Result)]);
    }
}
