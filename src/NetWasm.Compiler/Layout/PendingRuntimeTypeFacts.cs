using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed record RuntimeTypeNames(
    RuntimeTypeNamePayload Payload,
    string? Name,
    string? Namespace,
    string? FullName,
    string? DisplayName);

internal sealed record PendingRuntimeTypeFacts(
    CliTypeIdentity Identity,
    TypeDefinitionModel? Definition,
    int TypeId,
    int BaseTypeId,
    int AssignableTypeIdsAddress,
    int AssignableTypeIdCount,
    string? DelegateInvokeDescriptor,
    RuntimeTypeNames? Names)
{
    internal ImmutableArray<int> GenericArgumentTypeIds { get; init; } = [];
}

internal static class RuntimeTypeNameFormatter
{
    internal static RuntimeTypeNames Format(
        CliTypeIdentity identity,
        ITypeDefinitionResolver definitions,
        IAssemblyIdentityFormatter assemblies,
        RuntimeTypeNamePayload payload)
    {
        var names = FormatAll(identity, definitions, assemblies);
        return new(
            payload,
            (payload & RuntimeTypeNamePayload.Name) != 0 ? names.Name : null,
            (payload & RuntimeTypeNamePayload.Namespace) != 0 ? names.Namespace : null,
            (payload & RuntimeTypeNamePayload.FullName) != 0 ? names.FullName : null,
            (payload & RuntimeTypeNamePayload.DisplayName) != 0
                ? names.DisplayName
                : null);
    }

    private static RuntimeTypeNames FormatAll(
        CliTypeIdentity identity,
        ITypeDefinitionResolver definitions,
        IAssemblyIdentityFormatter assemblies) => identity.Shape switch
        {
            CliTypeShape.SzArray => Append(
                identity.ElementType!,
                "[]",
                definitions,
                assemblies),
            CliTypeShape.Array => Append(
                identity.ElementType!,
                identity.ArrayRank == 1
                    ? "[*]"
                    : "[" + new string(',', identity.ArrayRank - 1) + "]",
                definitions,
                assemblies),
            CliTypeShape.ManagedByReference => Append(
                identity.ElementType!,
                "&",
                definitions,
                assemblies),
            CliTypeShape.UnmanagedPointer => Append(
                identity.ElementType!,
                "*",
                definitions,
                assemblies),
            CliTypeShape.GenericTypeParameter or CliTypeShape.GenericMethodParameter =>
                new(
                    RuntimeTypeNamePayload.None,
                    identity.CanonicalName,
                    null,
                    null,
                    identity.CanonicalName),
            CliTypeShape.GenericInstantiation => FormatConstructed(
                identity,
                definitions,
                assemblies),
            _ => FormatDefinition(definitions.ResolveTypeIdentity(identity)),
        };

    private static RuntimeTypeNames FormatConstructed(
        CliTypeIdentity identity,
        ITypeDefinitionResolver definitions,
        IAssemblyIdentityFormatter assemblies)
    {
        var definition = definitions.ResolveTypeIdentity(identity);
        var arguments = identity.TypeArguments
            .Select(argument => (
                Identity: argument,
                Names: FormatAll(argument, definitions, assemblies)))
            .ToArray();
        var displayName = definition.FullName + "[" + string.Join(
            ",",
            arguments.Select(argument =>
                argument.Names.DisplayName!)) + "]";
        var fullName = identity.ContainsGenericParameters
            ? null
            : definition.FullName + "[" + string.Join(
                ",",
                arguments.Select(argument =>
                    "[" + AssemblyQualifiedName(
                        argument.Identity,
                        argument.Names,
                        definitions,
                        assemblies) + "]")) + "]";
        return new(
            RuntimeTypeNamePayload.None,
            SimpleName(definition.Name),
            EmptyToNull(definition.Namespace),
            fullName,
            displayName);
    }

    private static RuntimeTypeNames Append(
        CliTypeIdentity element,
        string suffix,
        ITypeDefinitionResolver definitions,
        IAssemblyIdentityFormatter assemblies)
    {
        var names = FormatAll(element, definitions, assemblies);
        return new(
            RuntimeTypeNamePayload.None,
            names.Name! + suffix,
            names.Namespace,
            names.FullName is null ? null : names.FullName + suffix,
            names.DisplayName! + suffix);
    }

    private static RuntimeTypeNames FormatDefinition(TypeDefinitionModel definition)
    {
        if (definition.GenericParameterNames.Length != definition.GenericArity)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.RuntimeContract,
                $"type definition '{definition.Key}' has inconsistent generic parameter names"));
        }

        var displayName = definition.GenericArity == 0
            ? definition.FullName
            : definition.FullName + "[" +
                string.Join(",", definition.GenericParameterNames) + "]";
        return new(
            RuntimeTypeNamePayload.None,
            SimpleName(definition.Name),
            EmptyToNull(definition.Namespace),
            definition.FullName,
            displayName);
    }

    private static string AssemblyQualifiedName(
        CliTypeIdentity identity,
        RuntimeTypeNames names,
        ITypeDefinitionResolver definitions,
        IAssemblyIdentityFormatter assemblies) =>
        names.FullName! + ", " +
        assemblies.Format(GetDefiningAssembly(identity, definitions));

    private static AssemblyIdentity GetDefiningAssembly(
        CliTypeIdentity identity,
        ITypeDefinitionResolver definitions) => identity.Shape switch
        {
            CliTypeShape.SzArray or
            CliTypeShape.Array or
            CliTypeShape.ManagedByReference or
            CliTypeShape.UnmanagedPointer or
            CliTypeShape.GenericInstantiation =>
                GetDefiningAssembly(identity.ElementType!, definitions),
            _ => definitions.ResolveTypeIdentity(identity).Key.Assembly,
        };

    private static string SimpleName(string metadataName)
    {
        var separator = metadataName.LastIndexOf('+');
        return separator < 0 ? metadataName : metadataName[(separator + 1)..];
    }

    private static string? EmptyToNull(string value) =>
        value.Length == 0 ? null : value;
}
