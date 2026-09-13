using System;
using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawResourceIntrinsicLayoutPlanner
{
    RawResourceIntrinsicLayout Plan(WitDocument document, RawWitImportDeclaration.Resource declaration, WasmTarget target);
}

public sealed class RawResourceIntrinsicLayoutPlanner(
    IWitCanonicalTypeResolver types,
    IRawCanonicalImportIdentityFormatter identities,
    ICanonicalAbiSignaturePlanner signatures) : IRawResourceIntrinsicLayoutPlanner
{
    private static readonly ImmutableDictionary<CanonicalAbiFunctionKind, bool> ReturnsHandle =
        ImmutableDictionary<CanonicalAbiFunctionKind, bool>.Empty
            .Add(CanonicalAbiFunctionKind.ImportedResourceDrop, false)
            .Add(CanonicalAbiFunctionKind.ExportedResourceNew, true)
            .Add(CanonicalAbiFunctionKind.ExportedResourceRep, true)
            .Add(CanonicalAbiFunctionKind.ExportedResourceDrop, false);
    private readonly IWitCanonicalTypeResolver _types = types ?? throw new ArgumentNullException(nameof(types));
    private readonly IRawCanonicalImportIdentityFormatter _identities = identities ?? throw new ArgumentNullException(nameof(identities));
    private readonly ICanonicalAbiSignaturePlanner _signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));

    public RawResourceIntrinsicLayout Plan(WitDocument document, RawWitImportDeclaration.Resource declaration, WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(declaration.Definition);
        ArgumentNullException.ThrowIfNull(declaration.InterfaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaration.Definition.Name);
        if (target is not (WasmTarget.Wasm32 or WasmTarget.Wasm64)) throw new ArgumentOutOfRangeException(nameof(target));
        if (!ReturnsHandle.TryGetValue(declaration.Kind, out var returnsHandle))
        {
            throw ComponentException.Invalid("raw resource import must use a supported resource intrinsic kind");
        }
        if (declaration.Definition.Kind.ValueKind != JsonValueKind.String || declaration.Definition.Kind.GetString() != "resource")
        {
            throw ComponentException.Invalid("raw resource intrinsic must reference a resource declaration, not an alias");
        }
        var handle = _types.Resolve(document, new WitTypeReference.Primitive("u32"));
        var function = new CanonicalAbiFunction(declaration.InterfaceName, declaration.Definition.Name, default,
            [new("handle", handle)], returnsHandle ? handle : null)
        {
            Kind = declaration.Kind,
            ResourceName = declaration.Definition.Name,
        };
        var identity = _identities.Format(function, target);
        var signature = _signatures.Plan(function, CanonicalAbiDirection.LoweredImport);
        return new(target, declaration, identity, signature);
    }
}
