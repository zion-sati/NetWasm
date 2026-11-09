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
                AddFunction(imports, document, "", "", worldSpecifier, item.Function, target);
                continue;
            }
            var definition = document.Interfaces[item.InterfaceId!.Value];
            var interfaceName = item.Name;
            var coreInterfaceName = item.CoreBindingName ?? interfaceName;
            var deploymentInterfaceName = item.Name;
            foreach (var function in definition.Functions)
            {
                AddFunction(imports, document, interfaceName, coreInterfaceName,
                    deploymentInterfaceName, function, target);
            }
            AddResources(imports, document, definition, interfaceName, coreInterfaceName,
                deploymentInterfaceName, CanonicalAbiFunctionKind.ImportedResourceDrop, target);
        }
        foreach (var item in world.Exports)
        {
            if (item.InterfaceId is not { } id) continue;
            var definition = document.Interfaces[id];
            var interfaceName = item.Name;
            var coreInterfaceName = item.CoreBindingName ?? interfaceName;
            var deploymentInterfaceName = item.Name;
            foreach (var kind in ExportedResourceImports)
            {
                AddResources(imports, document, definition, interfaceName, coreInterfaceName,
                    deploymentInterfaceName, kind, target);
            }
        }
        return new(document, world, target, imports.ToImmutable());
    }

    private void AddFunction(
        ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Builder imports,
        WitDocument document,
        string interfaceName,
        string coreInterfaceName,
        string deploymentInterfaceName,
        WitFunction function,
        WasmTarget target)
    {
        var canonical = new CanonicalAbiFunction(
            interfaceName, function.Name, default, [], null)
        {
            CoreInterfaceName = coreInterfaceName,
        };
        var identity = _identities.Format(canonical, target);
        Add(imports, identity, new RawWitImportDeclaration.Callable(
            interfaceName, deploymentInterfaceName, function)
        {
            CoreInterfaceName = coreInterfaceName,
        });

        // The Component Model names an async-lowered call and a signature's stream and
        // future built-ins by the function's full interface name ($root for a world's own).
        var module = interfaceName.Length == 0 ? "$root" : interfaceName;
        if (function.Kind.Name.StartsWith("async", StringComparison.Ordinal))
        {
            Add(imports, new(module, $"[async-lower]{function.Name}"),
                new RawWitImportDeclaration.Callable(interfaceName, deploymentInterfaceName, function)
                {
                    CoreInterfaceName = coreInterfaceName,
                    Asynchronous = true,
                });
        }
        var payloads = WitPayloadTypes.Find(document, function);
        for (var index = 0; index < payloads.Length; index++)
        {
            var stream = WitPayloadTypes.IsStream(document, payloads[index]);
            foreach (var operation in stream ? StreamOperations : FutureOperations)
            {
                Add(imports, new(module, PayloadName(operation, index, function.Name)),
                    new RawWitImportDeclaration.Payload(
                        interfaceName, deploymentInterfaceName, function, index, payloads[index], operation)
                    {
                        CoreInterfaceName = coreInterfaceName,
                    });
            }
        }
    }

    private static readonly ImmutableArray<RawPayloadOperation> StreamOperations =
    [
        RawPayloadOperation.StreamNew,
        RawPayloadOperation.StreamRead,
        RawPayloadOperation.StreamWrite,
        RawPayloadOperation.StreamDropReadable,
        RawPayloadOperation.StreamDropWritable,
    ];

    private static readonly ImmutableArray<RawPayloadOperation> FutureOperations =
    [
        RawPayloadOperation.FutureNew,
        RawPayloadOperation.FutureRead,
        RawPayloadOperation.FutureWrite,
        RawPayloadOperation.FutureDropReadable,
        RawPayloadOperation.FutureDropWritable,
    ];

    private static string PayloadName(RawPayloadOperation operation, int index, string function) => operation switch
    {
        RawPayloadOperation.StreamNew => $"[stream-new-{index}]{function}",
        RawPayloadOperation.StreamRead => $"[async-lower][stream-read-{index}]{function}",
        RawPayloadOperation.StreamWrite => $"[async-lower][stream-write-{index}]{function}",
        RawPayloadOperation.StreamDropReadable => $"[stream-drop-readable-{index}]{function}",
        RawPayloadOperation.StreamDropWritable => $"[stream-drop-writable-{index}]{function}",
        RawPayloadOperation.FutureNew => $"[future-new-{index}]{function}",
        RawPayloadOperation.FutureRead => $"[async-lower][future-read-{index}]{function}",
        RawPayloadOperation.FutureWrite => $"[async-lower][future-write-{index}]{function}",
        RawPayloadOperation.FutureDropReadable => $"[future-drop-readable-{index}]{function}",
        _ => $"[future-drop-writable-{index}]{function}",
    };

    private void AddResources(
        ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration>.Builder imports,
        WitDocument document,
        WitInterface definition,
        string interfaceName,
        string coreInterfaceName,
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
                CoreInterfaceName = coreInterfaceName,
                Kind = kind,
                ResourceName = resource.Name,
            };
            var identity = _identities.Format(canonical, target);
            Add(imports, identity, new RawWitImportDeclaration.Resource(
                interfaceName, deploymentInterfaceName, resource, kind)
            {
                CoreInterfaceName = coreInterfaceName,
            });
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
