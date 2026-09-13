using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentManifestBuilder
{
    ComponentManifest Build(
        WitDocument document,
        WitWorld world,
        ComponentTarget target,
        ComponentManifestInputs inputs);
}

public sealed class ComponentManifestBuilder(IWasmTools tools) : IComponentManifestBuilder
{
    private readonly IWasmTools _tools = tools ?? throw new ArgumentNullException(nameof(tools));

    public ComponentManifest Build(
        WitDocument document,
        WitWorld world,
        ComponentTarget target,
        ComponentManifestInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(inputs);
        if (target.WasiVersion != "0.2")
        {
            throw ComponentException.Invalid(
                $"unsupported WASI version '{target.WasiVersion}'; component packaging supports only WASI 0.2");
        }
        if (target.Width is not ("wasm32" or "wasm64"))
        {
            throw ComponentException.Invalid(
                $"unsupported component target width '{target.Width}'");
        }

        var version = _tools.Run("--version");
        if (version.ExitCode != 0 || string.IsNullOrWhiteSpace(version.StandardOutput))
        {
            throw ComponentException.Tool("wasm-tools did not report a usable version");
        }

        return new ComponentManifest(
            1,
            world.Package,
            world.Name,
            target.Width,
            target.WasiVersion,
            target.CanonicalStringEncoding,
            Convert.ToHexStringLower(SHA256.HashData(
                Encoding.UTF8.GetBytes(document.NormalizedJson))),
            version.StandardOutput.Trim(),
            InterfaceNames(document, world.Imports),
            InterfaceNames(document, world.Exports),
            FunctionNames(document, world.Imports),
            FunctionNames(document, world.Exports))
        {
            JavaScript = Normalize(inputs.JavaScript),
            Adapters = inputs.Adapters,
        };
    }

    private static ComponentJavaScriptBoundary Normalize(
        ComponentJavaScriptBoundary boundary) => new(
        [.. boundary.Imports.OrderBy(import => import.Module, StringComparer.Ordinal)
            .ThenBy(import => import.Name, StringComparer.Ordinal)],
        [.. boundary.Exports.OrderBy(export => export.Name, StringComparer.Ordinal)]);

    private static ImmutableArray<string> InterfaceNames(
        WitDocument document,
        ImmutableArray<WitWorldItem> items) => [.. items
        .Where(item => item.InterfaceId is not null)
        .Select(item => document.Interfaces[item.InterfaceId!.Value])
        .Select(item => $"{item.Package}/{item.Name}")
        .Order(StringComparer.Ordinal)];

    private static ImmutableArray<string> FunctionNames(
        WitDocument document,
        ImmutableArray<WitWorldItem> items)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var item in items)
        {
            if (item.Function is WitFunction function)
            {
                names.Add(item.Name);
                continue;
            }
            var @interface = document.Interfaces[item.InterfaceId!.Value];
            names.AddRange(@interface.Functions.Select(interfaceFunction =>
                $"{@interface.Package}/{@interface.Name}#{interfaceFunction.Name}"));
        }
        return [.. names.Order(StringComparer.Ordinal)];
    }
}
