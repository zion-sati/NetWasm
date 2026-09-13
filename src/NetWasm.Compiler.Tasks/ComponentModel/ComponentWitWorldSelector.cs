using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IComponentWitWorldSelector
{
    ComponentWitWorldSelection Select(ComponentWitWorldSelectionRequest request);
}

internal sealed record ComponentWitWorldSelection(
    string Path,
    string? World);

internal sealed record ComponentWitWorldVariant(
    string Path,
    string? World,
    string ActivationInterfacePrefix);

internal sealed record ComponentWitWorldSelectionRequest(
    string DefaultPath,
    string? DefaultWorld,
    ImmutableArray<ComponentWitWorldVariant> Variants,
    ImmutableArray<HostInteropWitImport> ReachableImports);

internal sealed class ComponentWitWorldSelector : IComponentWitWorldSelector
{
    public ComponentWitWorldSelection Select(ComponentWitWorldSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DefaultPath);

        var variants = request.Variants.IsDefault ? [] : request.Variants;
        var imports = request.ReachableImports.IsDefault ? [] : request.ReachableImports;
        foreach (var variant in variants)
        {
            ArgumentNullException.ThrowIfNull(variant);
            ArgumentException.ThrowIfNullOrWhiteSpace(variant.Path);
            ArgumentException.ThrowIfNullOrWhiteSpace(
                variant.ActivationInterfacePrefix);
        }
        var selected = variants
            .Where(variant => imports.Any(import => import.Interface.StartsWith(
                variant.ActivationInterfacePrefix,
                StringComparison.Ordinal)))
            .ToArray();
        if (selected.Length > 1)
        {
            throw new InvalidOperationException(
                "Reachable WIT imports activate multiple component-world variants.");
        }
        if (selected.Length == 0)
        {
            return new(request.DefaultPath, request.DefaultWorld);
        }

        var selectedVariant = selected[0];
        return new(selectedVariant.Path, selectedVariant.World);
    }
}
