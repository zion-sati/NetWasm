using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawWitImportCatalogBuilder
{
    RawWitImportCatalog Build(WitDocument document, WitWorld world, WasmTarget target);
}

public sealed class RawWitImportCatalogBuilder(
    IWitWorldValidator worlds,
    IRawCanonicalImportIdentityFormatter identities,
    IWitWorldSpecifierFormatter worldSpecifiers,
    IWitInterfaceSpecifierFormatter interfaceSpecifiers) : IRawWitImportCatalogBuilder
{
    private static readonly ImmutableArray<CanonicalAbiFunctionKind> ExportedResourceImports =
    [
        CanonicalAbiFunctionKind.ExportedResourceNew,
        CanonicalAbiFunctionKind.ExportedResourceRep,
        CanonicalAbiFunctionKind.ExportedResourceDrop,
    ];
    private readonly IWitWorldValidator _worlds = worlds ?? throw new ArgumentNullException(nameof(worlds));
    private readonly IRawCanonicalImportIdentityFormatter _identities = identities ?? throw new ArgumentNullException(nameof(identities));
    private readonly IWitWorldSpecifierFormatter _worldSpecifiers = worldSpecifiers ??
        throw new ArgumentNullException(nameof(worldSpecifiers));
    private readonly IWitInterfaceSpecifierFormatter _interfaceSpecifiers = interfaceSpecifiers ??
        throw new ArgumentNullException(nameof(interfaceSpecifiers));

    public RawWitImportCatalog Build(WitDocument document, WitWorld world, WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        if (target is not (WasmTarget.Wasm32 or WasmTarget.Wasm64))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
        if (world.Imports.IsDefault || world.Exports.IsDefault || document.Interfaces.IsDefault || document.Types.IsDefault)
        {
            throw ComponentException.Invalid("raw WIT catalog inputs must contain explicit collections");
        }
        foreach (var item in world.Imports.Concat(world.Exports))
        {
            if (item is null || (item.Function is null) == (item.InterfaceId is null))
            {
                throw ComponentException.Invalid("WIT world item must declare exactly one function or interface");
            }
        }
        _worlds.Validate(document, world);
        var imports = ImmutableDictionary.CreateBuilder<RawCanonicalImportIdentity, RawWitImportDeclaration>();
        var worldSpecifier = _worldSpecifiers.Format(world);
        foreach (var item in world.Imports)
        {
            if (item.Function is not null)
            {
                AddFunction(imports, "", worldSpecifier, item.Function, target);
                continue;
            }
            var definition = document.Interfaces[item.InterfaceId!.Value];
            var interfaceName = $"{definition.Package}/{definition.Name}";
            var deploymentInterfaceName = _interfaceSpecifiers.Format(definition);
            foreach (var function in definition.Functions)
            {
                AddFunction(imports, interfaceName, deploymentInterfaceName, function, target);
            }
            AddResources(imports, document, definition, interfaceName,
                deploymentInterfaceName, CanonicalAbiFunctionKind.ImportedResourceDrop, target);
        }
        foreach (var item in world.Exports)
        {
            if (item.InterfaceId is not { } id) continue;
            var definition = document.Interfaces[id];
            var interfaceName = $"{definition.Package}/{definition.Name}";
            var deploymentInterfaceName = _interfaceSpecifiers.Format(definition);
            foreach (var kind in ExportedResourceImports)
            {
                AddResources(imports, document, definition, interfaceName,
                    deploymentInterfaceName, kind, target);
            }
        }
        return new(document, world, target, imports.ToImmutable());
    }

    private void AddFunction(
        ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Builder imports,
        string interfaceName,
        string deploymentInterfaceName,
        WitFunction function,
        WasmTarget target)
    {
        var identity = _identities.Format(new(interfaceName, function.Name, default, [], null), target);
        Add(imports, identity, new RawWitImportDeclaration.Callable(
            interfaceName, deploymentInterfaceName, function));
    }

    private void AddResources(
        ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Builder imports,
        WitDocument document,
        WitInterface definition,
        string interfaceName,
        string deploymentInterfaceName,
        CanonicalAbiFunctionKind kind,
        WasmTarget target)
    {
        foreach (var id in definition.Types.Values)
        {
            var resource = document.Types[id];
            if (resource.Kind.ValueKind != JsonValueKind.String || resource.Kind.GetString() != "resource") continue;
            ArgumentException.ThrowIfNullOrWhiteSpace(resource.Name);
            var canonical = new CanonicalAbiFunction(interfaceName, resource.Name, default, [], null)
            {
                Kind = kind,
                ResourceName = resource.Name,
            };
            var identity = _identities.Format(canonical, target);
            Add(imports, identity, new RawWitImportDeclaration.Resource(
                interfaceName, deploymentInterfaceName, resource, kind));
        }
    }

    private static void Add(
        ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Builder imports,
        RawCanonicalImportIdentity identity, RawWitImportDeclaration declaration)
    {
        if (!imports.TryAdd(identity, declaration))
        {
            throw ComponentException.Invalid("raw WIT imports have colliding physical identities");
        }
    }
}
