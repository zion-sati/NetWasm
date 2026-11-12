using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Symbols;

// Forms a type's Name, Namespace, FullName and ToString() text as .NET does, with the display name of the
// assembly .NET writes for each generic argument in FullName.
internal static class RuntimeTypeNameFormatter
{
    internal static RuntimeTypeSymbol Format(
        int typeId,
        CliTypeIdentity identity,
        ITypeDefinitionResolver definitions,
        IReadOnlyDictionary<CliTypeIdentity, string> genericParameterNames,
        Func<CliTypeIdentity, string> assemblyName)
    {
        var names = FormatAll(identity, definitions, genericParameterNames, assemblyName);
        return new(typeId, names.Name, names.Namespace, names.FullName, names.DisplayName);
    }

    private static Names FormatAll(
        CliTypeIdentity identity,
        ITypeDefinitionResolver definitions,
        IReadOnlyDictionary<CliTypeIdentity, string> genericParameterNames,
        Func<CliTypeIdentity, string> assemblyName) => identity.Shape switch
        {
            CliTypeShape.SzArray => Append(identity.ElementType!, "[]", definitions, genericParameterNames, assemblyName),
            CliTypeShape.Array => Append(
                identity.ElementType!,
                identity.ArrayRank == 1 ? "[*]" : "[" + new string(',', identity.ArrayRank - 1) + "]",
                definitions,
                genericParameterNames,
                assemblyName),
            CliTypeShape.ManagedByReference => Append(identity.ElementType!, "&", definitions, genericParameterNames, assemblyName),
            CliTypeShape.UnmanagedPointer => Append(identity.ElementType!, "*", definitions, genericParameterNames, assemblyName),
            CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter =>
                GenericParameter(identity, genericParameterNames),
            CliTypeShape.GenericInstantiation => FormatConstructed(identity, definitions, genericParameterNames, assemblyName),
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
        IReadOnlyDictionary<CliTypeIdentity, string> genericParameterNames,
        Func<CliTypeIdentity, string> assemblyName)
    {
        var definition = definitions.ResolveTypeIdentity(identity);
        var arguments = identity.TypeArguments
            .Select(argument => FormatAll(argument, definitions, genericParameterNames, assemblyName))
            .ToArray();
        var displayName = definition.FullName + "[" +
            string.Join(",", arguments.Select(argument => argument.DisplayName)) + "]";
        var fullName = identity.ContainsGenericParameters
            ? null
            : definition.FullName + "[" + string.Join(
                ",",
                identity.TypeArguments.Select((argument, index) =>
                    "[" + arguments[index].FullName + ", " + assemblyName(argument) + "]")) + "]";
        return new(SimpleName(definition.Name), EmptyToNull(definition.Namespace), fullName, displayName);
    }

    private static Names Append(
        CliTypeIdentity element,
        string suffix,
        ITypeDefinitionResolver definitions,
        IReadOnlyDictionary<CliTypeIdentity, string> genericParameterNames,
        Func<CliTypeIdentity, string> assemblyName)
    {
        var names = FormatAll(element, definitions, genericParameterNames, assemblyName);
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
