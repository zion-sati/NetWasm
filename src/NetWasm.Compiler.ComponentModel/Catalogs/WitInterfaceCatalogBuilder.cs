using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Compiler.ComponentModel.Catalogs;

public interface IWitInterfaceCatalogBuilder
{
    WitInterfaceCatalog Build(WitDocument document, WitWorld world);
}

public sealed class WitInterfaceCatalogBuilder(
    IWitWorldValidator worlds,
    IWitWorldSpecifierFormatter worldSpecifiers,
    IWitInterfaceSpecifierFormatter interfaceSpecifiers,
    IWitTypeIdentityFormatter types) : IWitInterfaceCatalogBuilder
{
    private readonly IWitWorldValidator _worlds = worlds ??
        throw new ArgumentNullException(nameof(worlds));
    private readonly IWitWorldSpecifierFormatter _worldSpecifiers = worldSpecifiers ??
        throw new ArgumentNullException(nameof(worldSpecifiers));
    private readonly IWitInterfaceSpecifierFormatter _interfaceSpecifiers = interfaceSpecifiers ??
        throw new ArgumentNullException(nameof(interfaceSpecifiers));
    private readonly IWitTypeIdentityFormatter _types = types ??
        throw new ArgumentNullException(nameof(types));

    public WitInterfaceCatalog Build(WitDocument document, WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        if (document.Interfaces.IsDefault || document.Types.IsDefault
            || world.Imports.IsDefault || world.Exports.IsDefault)
        {
            throw ComponentException.Invalid(
                "WIT interface catalog inputs must contain explicit collections");
        }
        if (string.IsNullOrWhiteSpace(document.NormalizedJson))
        {
            throw ComponentException.Invalid("WIT interface catalog requires normalized source JSON");
        }

        ValidateWorldItems(document, world);
        _worlds.Validate(document, world);
        var modules = new HashSet<string>(StringComparer.Ordinal);
        var interfaces = ImmutableArray.CreateBuilder<WitInterfaceContract>();
        foreach (var item in world.Imports)
        {
            if (item.InterfaceId is not { } interfaceId || item.Function is not null)
            {
                throw ComponentException.Invalid(
                    "WIT provider catalog supports only valid interface imports");
            }

            var definition = document.Interfaces[interfaceId]!;
            var module = _interfaceSpecifiers.Format(definition);
            if (!modules.Add(module))
            {
                throw ComponentException.Invalid(
                    $"WIT provider interface '{module}' is imported more than once");
            }
            interfaces.Add(new(module, BuildFunctions(document, definition, module)));
        }

        return new(
            _worldSpecifiers.Format(world),
            document.NormalizedJson,
            [.. interfaces.OrderBy(value => value.Module, StringComparer.Ordinal)]);
    }

    private static void ValidateWorldItems(WitDocument document, WitWorld world)
    {
        foreach (var item in world.Imports.Concat(world.Exports))
        {
            if (item is null
                || (item.InterfaceId is null) == (item.Function is null)
                || item.InterfaceId is { } interfaceId
                    && ((uint)interfaceId >= (uint)document.Interfaces.Length
                        || document.Interfaces[interfaceId] is null))
            {
                throw ComponentException.Invalid(
                    "WIT provider catalog world contains an invalid item");
            }
        }
    }

    private ImmutableArray<WitInterfaceFunction> BuildFunctions(
        WitDocument document,
        WitInterface definition,
        string module)
    {
        if (definition.Functions.IsDefault)
        {
            throw ComponentException.Invalid(
                $"WIT provider interface '{module}' must declare an explicit function inventory");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var functions = ImmutableArray.CreateBuilder<WitInterfaceFunction>();
        foreach (var function in definition.Functions)
        {
            if (function is null || string.IsNullOrWhiteSpace(function.Name)
                || function.Parameters.IsDefault || !names.Add(function.Name))
            {
                throw ComponentException.Invalid(
                    $"WIT provider interface '{module}' has an invalid function inventory");
            }
            functions.Add(new(
                module,
                function.Name,
                [.. function.Parameters.Select(parameter =>
                {
                    if (parameter is null)
                    {
                        throw ComponentException.Invalid(
                            $"WIT provider function '{function.Name}' has an invalid parameter");
                    }
                    return _types.Format(document, parameter.Type ?? throw ComponentException.Invalid(
                        $"WIT provider function '{function.Name}' has an invalid parameter type"));
                })],
                function.Result is null ? [] : [_types.Format(document, function.Result)]));
        }
        return [.. functions.OrderBy(value => value.Name, StringComparer.Ordinal)];
    }
}
