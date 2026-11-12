using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Symbols;

// Forms a type's Name, Namespace, FullName and ToString() text as .NET does. A program has no assemblies at run
// time, so wherever .NET writes an assembly name (a generic argument's in FullName) the text is
// AssemblyNameUnavailable.
internal static class RuntimeTypeNameFormatter
{
    internal const string AssemblyNameUnavailable = "<Assembly name unavailable>";

    internal static RuntimeTypeSymbol Format(
        int typeId,
        CliTypeIdentity identity,
        ITypeDefinitionResolver definitions,
        IReadOnlyDictionary<CliTypeIdentity, string> genericParameterNames)
    {
        var names = FormatAll(identity, definitions, genericParameterNames);
        return new(typeId, names.Name, names.Namespace, names.FullName, names.DisplayName);
    }

    private static Names FormatAll(
        CliTypeIdentity identity,
        ITypeDefinitionResolver definitions,
        IReadOnlyDictionary<CliTypeIdentity, string> genericParameterNames) => identity.Shape switch
        {
            CliTypeShape.SzArray => Append(identity.ElementType!, "[]", definitions, genericParameterNames),
            CliTypeShape.Array => Append(
                identity.ElementType!,
                identity.ArrayRank == 1 ? "[*]" : "[" + new string(',', identity.ArrayRank - 1) + "]",
                definitions,
                genericParameterNames),
            CliTypeShape.ManagedByReference => Append(identity.ElementType!, "&", definitions, genericParameterNames),
            CliTypeShape.UnmanagedPointer => Append(identity.ElementType!, "*", definitions, genericParameterNames),
            CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter =>
                GenericParameter(identity, genericParameterNames),
            CliTypeShape.GenericInstantiation => FormatConstructed(identity, definitions, genericParameterNames),
            _ => FormatDefinition(definitions.ResolveTypeIdentity(identity)),
        };

    // A generic parameter has no namespace or full name; its name is the declared one (T), or its position
    // when the compiler kept no declared name.
    private static Names GenericParameter(
        CliTypeIdentity identity,
        IReadOnlyDictionary<CliTypeIdentity, string> genericParameterNames)
    {
        var name = genericParameterNames.TryGetValue(identity, out var declared)
            ? declared
            : (identity.Shape == CliTypeShape.GenericMethodParameter ? "!!" : "!") + identity.GenericParameterIndex;
        return new(name, null, null, name);
    }

    private static Names FormatConstructed(
        CliTypeIdentity identity,
        ITypeDefinitionResolver definitions,
        IReadOnlyDictionary<CliTypeIdentity, string> genericParameterNames)
    {
        var definition = definitions.ResolveTypeIdentity(identity);
        var arguments = identity.TypeArguments
            .Select(argument => FormatAll(argument, definitions, genericParameterNames))
            .ToArray();
        var displayName = definition.FullName + "[" +
            string.Join(",", arguments.Select(argument => argument.DisplayName)) + "]";
        var fullName = identity.ContainsGenericParameters
            ? null
            : definition.FullName + "[" + string.Join(
                ",",
                arguments.Select(argument =>
                    "[" + argument.FullName + ", " + AssemblyNameUnavailable + "]")) + "]";
        return new(SimpleName(definition.Name), EmptyToNull(definition.Namespace), fullName, displayName);
    }

    private static Names Append(
        CliTypeIdentity element,
        string suffix,
        ITypeDefinitionResolver definitions,
        IReadOnlyDictionary<CliTypeIdentity, string> genericParameterNames)
    {
        var names = FormatAll(element, definitions, genericParameterNames);
        return new(
            names.Name + suffix,
            names.Namespace,
            names.FullName is null ? null : names.FullName + suffix,
            names.DisplayName + suffix);
    }

    private static Names FormatDefinition(TypeDefinitionModel definition)
    {
        if (definition.GenericParameterNames.Length != definition.GenericArity)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"type definition '{definition.Key}' has inconsistent generic parameter names"));
        }

        var displayName = definition.GenericArity == 0
            ? definition.FullName
            : definition.FullName + "[" + string.Join(",", definition.GenericParameterNames) + "]";
        return new(SimpleName(definition.Name), EmptyToNull(definition.Namespace), definition.FullName, displayName);
    }

    private static string SimpleName(string metadataName)
    {
        var separator = metadataName.LastIndexOf('+');
        return separator < 0 ? metadataName : metadataName[(separator + 1)..];
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private readonly record struct Names(string Name, string? Namespace, string? FullName, string DisplayName);
}
