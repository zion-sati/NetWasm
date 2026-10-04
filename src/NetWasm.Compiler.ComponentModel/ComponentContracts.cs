using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace NetWasm.Compiler.ComponentModel;

public sealed record ComponentTarget(
    string Width,
    string WasiVersion,
    string CanonicalStringEncoding)
{
    public static ComponentTarget Wasm32Wasi02 { get; } =
        new("wasm32", "0.2", "utf8");

    public static ComponentTarget Wasm64Wasi02 { get; } =
        new("wasm64", "0.2", "utf8");
}

public sealed record WitFunction(
    string Name,
    ImmutableArray<WitParameter> Parameters,
    WitTypeReference? Result,
    WitFunctionKind Kind);

public sealed record WitParameter(string Name, WitTypeReference Type);

public sealed record WitFunctionKind(string Name, int? ResourceType = null);

public abstract record WitTypeReference
{
    public sealed record Primitive(string Name) : WitTypeReference;

    public sealed record Defined(int Id) : WitTypeReference;
}

public sealed record WitInterface(
    int Id,
    string Name,
    string Package,
    ImmutableDictionary<string, int> Types,
    ImmutableArray<WitFunction> Functions);

public sealed record WitWorldItem(
    string Name,
    int? InterfaceId,
    WitFunction? Function)
{
    public string? CoreBindingName { get; init; }
}

public sealed record WitWorld(
    int Id,
    string Name,
    string Package,
    ImmutableArray<WitWorldItem> Imports,
    ImmutableArray<WitWorldItem> Exports);

public sealed record WitTypeDefinition(
    int Id,
    string? Name,
    JsonElement Kind,
    int? OwnerInterface);

public sealed record WitPackage(
    int Id,
    string Name,
    ImmutableDictionary<string, int> Interfaces,
    ImmutableDictionary<string, int> Worlds);

public sealed record WitDocument(
    ImmutableArray<WitPackage> Packages,
    ImmutableArray<WitInterface> Interfaces,
    ImmutableArray<WitWorld> Worlds,
    ImmutableArray<WitTypeDefinition> Types,
    string NormalizedJson)
{
    public WitWorld SelectWorld(string? requestedWorld)
    {
        if (requestedWorld is null)
        {
            return Worlds.Length == 1
                ? Worlds[0]
                : throw ComponentException.Invalid(
                    "WIT input must contain exactly one world when no world is selected");
        }

        var matches = Worlds.Where(world =>
                string.Equals(world.Name, requestedWorld, StringComparison.Ordinal) ||
                string.Equals($"{world.Package}/{world.Name}", requestedWorld,
                    StringComparison.Ordinal) ||
                string.Equals(FormatWorldSpecifier(world), requestedWorld,
                    StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw ComponentException.Invalid(
                $"WIT world '{requestedWorld}' was not found or is ambiguous");
    }

    private static string FormatWorldSpecifier(WitWorld world)
    {
        var versionIndex = world.Package.LastIndexOf('@');
        return versionIndex < 0
            ? $"{world.Package}/{world.Name}"
            : $"{world.Package[..versionIndex]}/{world.Name}{world.Package[versionIndex..]}";
    }
}

internal static class ComponentException
{
    public static Compiler.Core.CompilerException Invalid(string message) => new(
        new Compiler.Core.CompilerDiagnostic(
            Compiler.Core.DiagnosticCode.ComponentContract,
            message));

    public static Compiler.Core.CompilerException Tool(string message) => new(
        new Compiler.Core.CompilerDiagnostic(
            Compiler.Core.DiagnosticCode.ComponentToolchain,
            message));
}
