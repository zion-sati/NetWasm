using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Worlds;

namespace NetWasm.Compiler.ComponentModel.Catalogs;

public sealed record WitWorldFunctionProjection(
    ImmutableArray<WitInterfaceFunction> Imports,
    ImmutableArray<WitInterfaceFunction> Exports,
    ImmutableArray<string> ImportModules,
    WitWorkerContract WorkerContract);

public interface IWitWorldFunctionProjector
{
    WitWorldFunctionProjection Project(
        WitDocument document,
        WitWorld world,
        WitWorld? applicationWorld = null);

    WitWorldFunctionProjection Project(
        WitDocument document,
        WitWorld world,
        WitDocument applicationDocument,
        WitWorld applicationWorld,
        WitWorld sourceWorkerWorld);
}

public sealed class WitWorldFunctionProjector(
    IWitWorldValidator worlds,
    IWitWorldSpecifierFormatter worldSpecifiers,
    IWitInterfaceSpecifierFormatter interfaceSpecifiers,
    IWitTypeIdentityFormatter types,
    IWitJavaScriptNameFormatter javaScriptNames,
    IWitWorkerTypeProjector workerTypes) : IWitWorldFunctionProjector
{
    private const string ReactorGuestInterface =
        "netwasm:runtime/reactor-guest@1.0.0";
    private const string ReactorHostInterface =
        "netwasm:runtime/reactor-host@1.0.0";
    private const string ReactorWakeFunction = "wake";
    private readonly IWitWorldValidator _worlds = worlds ??
        throw new ArgumentNullException(nameof(worlds));
    private readonly IWitWorldSpecifierFormatter _worldSpecifiers = worldSpecifiers ??
        throw new ArgumentNullException(nameof(worldSpecifiers));
    private readonly IWitInterfaceSpecifierFormatter _interfaceSpecifiers = interfaceSpecifiers ??
        throw new ArgumentNullException(nameof(interfaceSpecifiers));
    private readonly IWitTypeIdentityFormatter _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly IWitJavaScriptNameFormatter _javaScriptNames = javaScriptNames ??
        throw new ArgumentNullException(nameof(javaScriptNames));
    private readonly IWitWorkerTypeProjector _workerTypes = workerTypes ??
        throw new ArgumentNullException(nameof(workerTypes));

    public WitWorldFunctionProjection Project(
        WitDocument document,
        WitWorld world,
        WitWorld? applicationWorld = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        applicationWorld ??= world;
        return Project(document, world, document, applicationWorld, world);
    }

    public WitWorldFunctionProjection Project(
        WitDocument document,
        WitWorld world,
        WitDocument applicationDocument,
        WitWorld applicationWorld,
        WitWorld sourceWorkerWorld)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(applicationDocument);
        ArgumentNullException.ThrowIfNull(applicationWorld);
        ArgumentNullException.ThrowIfNull(sourceWorkerWorld);
        _worlds.Validate(document, world);
        _worlds.Validate(applicationDocument, applicationWorld);
        _worlds.Validate(applicationDocument, sourceWorkerWorld);
        if (!ReferenceEquals(applicationWorld, sourceWorkerWorld))
        {
            ValidateApplicationWorldSubset(
                applicationDocument,
                sourceWorkerWorld,
                applicationDocument,
                applicationWorld,
                requireExactItemName: true);
        }
        if (!ReferenceEquals(applicationDocument, document)
            || !ReferenceEquals(applicationWorld, world))
        {
            ValidateApplicationWorldSubset(
                document,
                world,
                applicationDocument,
                applicationWorld,
                requireExactItemName: false);
        }
        return new(
            ProjectItems(document, world, world.Imports),
            ProjectItems(
                applicationDocument,
                applicationWorld,
                applicationWorld.Exports),
            ProjectModules(document, world, world.Imports),
            ProjectWorkerContract(
                applicationDocument,
                applicationWorld,
                document,
                world));
    }

    private WitWorkerContract ProjectWorkerContract(
        WitDocument document,
        WitWorld world,
        WitDocument packagedDocument,
        WitWorld packagedWorld)
    {
        // ProjectItems validates this same export inventory before the worker
        // contract is built, including every referenced interface and function.
        var exports = ImmutableArray.CreateBuilder<WitWorkerExport>();
        foreach (var item in world.Exports)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.Function is { } rootFunction)
            {
                exports.Add(ProjectWorkerExport(
                    item.Name,
                    "root",
                    item.Name,
                    null,
                    string.Empty,
                    item.Name,
                    rootFunction));
                continue;
            }

            var id = item.InterfaceId!.Value;
            var definition = document.Interfaces[id]!;
            var specifier = _interfaceSpecifiers.Format(definition);
            var javaScriptRoot = ProjectInterfaceJavaScriptRoot(item);
            foreach (var function in definition.Functions)
            {
                ArgumentNullException.ThrowIfNull(function);
                exports.Add(ProjectWorkerExport(
                    string.Concat(specifier, "/", function.Name),
                    "interface",
                    item.Name,
                    specifier,
                    javaScriptRoot,
                    function.Name,
                    function));
            }
        }

        var ordered = exports
            .OrderBy(export => export.Operation, StringComparer.Ordinal)
            .ToImmutableArray();
        return new(
            2,
            _worldSpecifiers.Format(world),
            ordered,
            _workerTypes.Project(document, ordered),
            ProjectReactor(packagedDocument, packagedWorld));
    }

    private WitWorkerExport ProjectWorkerExport(
        string operation,
        string placement,
        string worldItem,
        string? interfaceName,
        string javaScriptRoot,
        string javaScriptMember,
        WitFunction function)
    {
        if (string.IsNullOrWhiteSpace(operation)
            || string.IsNullOrWhiteSpace(worldItem)
            || string.IsNullOrWhiteSpace(function.Name)
            || function.Parameters.IsDefault)
        {
            throw ComponentException.Invalid("WIT worker export is incomplete");
        }
        return new(
            operation,
            placement,
            worldItem,
            interfaceName,
            javaScriptRoot,
            _javaScriptNames.FormatMember(javaScriptMember),
            function.Name,
            [.. function.Parameters.Select(parameter =>
            {
                ArgumentNullException.ThrowIfNull(parameter);
                if (string.IsNullOrWhiteSpace(parameter.Name))
                {
                    throw ComponentException.Invalid(
                        "WIT worker export parameter is incomplete");
                }
                return new WitWorkerParameter(
                    parameter.Name,
                    ProjectTypeReference(parameter.Type));
            })],
            function.Result is null ? null : ProjectTypeReference(function.Result),
            function.Kind);
    }

    private static WitWorkerTypeReference ProjectTypeReference(WitTypeReference type)
    {
        if (type is WitTypeReference.Primitive primitive)
        {
            return new("primitive", primitive.Name, null);
        }
        var defined = (WitTypeReference.Defined)type;
        return new("defined", null, defined.Id);
    }

    private string ProjectInterfaceJavaScriptRoot(WitWorldItem item) =>
        _javaScriptNames.FormatMember(item.Name);

    private WitWorkerReactor? ProjectReactor(
        WitDocument document,
        WitWorld packagedWorld)
    {
        WitWorkerReactor? reactor = null;
        var hostCount = 0;
        foreach (var item in packagedWorld.Imports)
        {
            if (item.InterfaceId is not { } id)
            {
                continue;
            }
            var definition = document.Interfaces[id]!;
            if (_interfaceSpecifiers.Format(definition) == ReactorHostInterface)
            {
                hostCount++;
            }
        }
        foreach (var item in packagedWorld.Exports)
        {
            if (item.InterfaceId is not { } id)
            {
                continue;
            }
            var definition = document.Interfaces[id]!;
            var specifier = _interfaceSpecifiers.Format(definition);
            if (specifier != ReactorGuestInterface)
            {
                continue;
            }
            var wakes = definition.Functions.Where(function =>
                    function.Name == ReactorWakeFunction)
                .ToArray();
            if (wakes.Length != 1 || wakes[0].Kind.Name != "freestanding")
            {
                throw ComponentException.Invalid(
                    "WIT worker reactor-guest export is incomplete");
            }
            if (reactor is not null)
            {
                throw ComponentException.Invalid(
                    "WIT worker reactor-guest export is duplicated");
            }
            reactor = new(
                specifier,
                ProjectInterfaceJavaScriptRoot(item),
                _javaScriptNames.FormatMember(wakes[0].Name));
        }
        if (reactor is null && hostCount == 0)
        {
            return null;
        }
        if (reactor is null || hostCount > 1)
        {
            throw ComponentException.Invalid(
                "WIT worker reactor-host imports require exactly one reactor-guest export and may be declared at most once");
        }
        return reactor;
    }

    private void ValidateApplicationWorldSubset(
        WitDocument packagedDocument,
        WitWorld packagedWorld,
        WitDocument applicationDocument,
        WitWorld applicationWorld,
        bool requireExactItemName)
    {
        if (packagedWorld.Exports.IsDefault || applicationWorld.Exports.IsDefault)
        {
            throw ComponentException.Invalid(
                "WIT worker export inventories must be explicit");
        }
        foreach (var applicationItem in applicationWorld.Exports)
        {
            ArgumentNullException.ThrowIfNull(applicationItem);
            var packagedItems = packagedWorld.Exports.Where(item =>
                    EquivalentIdentity(
                        packagedDocument,
                        item!,
                        applicationDocument,
                        applicationItem,
                        requireExactItemName))
                .ToArray();
            if (packagedItems.Length != 1
                || !Equivalent(
                    packagedDocument,
                    packagedItems[0],
                    applicationDocument,
                    applicationItem))
            {
                throw ComponentException.Invalid(
                    "The selected WIT application world is not an exact export subset of the packaged worker world");
            }
        }
    }

    private bool Equivalent(
        WitDocument leftDocument,
        WitWorldItem left,
        WitDocument rightDocument,
        WitWorldItem right)
    {
        // EquivalentIdentity has already established a well-formed matching
        // placement before this deeper contract comparison runs.
        if (left.InterfaceId is { } leftId)
        {
            var rightId = right.InterfaceId!.Value;
            var leftInterface = leftDocument.Interfaces[leftId]!;
            var rightInterface = rightDocument.Interfaces[rightId]!;
            return Equivalent(leftDocument, leftInterface, rightDocument, rightInterface);
        }
        return Equivalent(leftDocument, left.Function!, rightDocument, right.Function!);
    }

    private bool EquivalentIdentity(
        WitDocument leftDocument,
        WitWorldItem left,
        WitDocument rightDocument,
        WitWorldItem right,
        bool requireExactItemName)
    {
        if (left.InterfaceId is not null || right.InterfaceId is not null)
        {
            if (left.InterfaceId is not { } leftId
                || right.InterfaceId is not { } rightId
                || left.Function is not null
                || right.Function is not null
                || (uint)leftId >= (uint)leftDocument.Interfaces.Length
                || (uint)rightId >= (uint)rightDocument.Interfaces.Length)
            {
                return false;
            }
            var leftInterface = leftDocument.Interfaces[leftId];
            var rightInterface = rightDocument.Interfaces[rightId];
            return leftInterface is not null
                && rightInterface is not null
                && (!requireExactItemName || string.Equals(
                    left.Name,
                    right.Name,
                    StringComparison.Ordinal))
                && _interfaceSpecifiers.Format(leftInterface)
                    == _interfaceSpecifiers.Format(rightInterface);
        }
        return left.Function is not null
            && right.Function is not null
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal);
    }

    private bool Equivalent(
        WitDocument leftDocument,
        WitInterface left,
        WitDocument rightDocument,
        WitInterface right)
    {
        if (left.Functions.IsDefault || right.Functions.IsDefault
            || left.Functions.Length != right.Functions.Length)
        {
            return false;
        }
        var rightFunctions = right.Functions.ToDictionary(
            function => function.Name,
            StringComparer.Ordinal);
        return left.Functions.All(function =>
            rightFunctions.TryGetValue(function.Name, out var candidate)
            && Equivalent(leftDocument, function, rightDocument, candidate));
    }

    private bool Equivalent(
        WitDocument leftDocument,
        WitFunction left,
        WitDocument rightDocument,
        WitFunction right) =>
        left.Name == right.Name
        && EquivalentFunctionKind(
            leftDocument,
            left.Kind,
            rightDocument,
            right.Kind)
        && !left.Parameters.IsDefault
        && !right.Parameters.IsDefault
        && left.Parameters.Length == right.Parameters.Length
        && left.Parameters.Zip(right.Parameters).All(pair =>
            pair.First.Name == pair.Second.Name
            && _types.Format(leftDocument, pair.First.Type)
                == _types.Format(rightDocument, pair.Second.Type))
        && (left.Result is null && right.Result is null
            || left.Result is not null
                && right.Result is not null
                && _types.Format(leftDocument, left.Result)
                    == _types.Format(rightDocument, right.Result));

    private bool EquivalentFunctionKind(
        WitDocument leftDocument,
        WitFunctionKind left,
        WitDocument rightDocument,
        WitFunctionKind right)
    {
        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal))
        {
            return false;
        }
        if (left.ResourceType is null || right.ResourceType is null)
        {
            return left.ResourceType is null && right.ResourceType is null;
        }
        return _types.Format(
                leftDocument,
                new WitTypeReference.Defined(left.ResourceType.Value))
            == _types.Format(
                rightDocument,
                new WitTypeReference.Defined(right.ResourceType.Value));
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
