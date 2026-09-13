using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Interop;

public interface IComponentContractResolver
{
    ComponentBoundaryContract Resolve(
        MetadataCompilationSnapshot metadata,
        ISymbolFormatter symbols,
        CompilerOptions options);
}

internal sealed class ComponentContractResolver(
    IWitDocumentReader documents,
    ICanonicalAbiSignaturePlanner signatures,
    IWitCanonicalTypeResolver canonicalTypes,
    IWitManagedBindingSelector bindings) : IComponentContractResolver
{
    private static readonly EntityKey UnboundImport = new(
        new AssemblyIdentity("<unbound-wit-import>"),
        0);

    private readonly IWitDocumentReader _documents = documents ??
        throw new ArgumentNullException(nameof(documents));
    private readonly ICanonicalAbiSignaturePlanner _signatures = signatures ??
        throw new ArgumentNullException(nameof(signatures));
    private readonly IWitCanonicalTypeResolver _canonicalTypes = canonicalTypes ??
        throw new ArgumentNullException(nameof(canonicalTypes));
    private readonly IWitManagedBindingSelector _bindings = bindings ??
        throw new ArgumentNullException(nameof(bindings));

    public ComponentBoundaryContract Resolve(
        MetadataCompilationSnapshot metadata,
        ISymbolFormatter symbols,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(options);
        if (options.WitPath is null)
        {
            if (options.WitWorld is not null)
            {
                throw Invalid("a WIT world cannot be selected without a WIT path");
            }
            return ComponentBoundaryContract.Empty;
        }

        var document = _documents.Read(options.WitPath);
        var world = document.SelectWorld(options.WitWorld);
        var imports = ResolveFunctions(
                metadata,
                symbols,
                document,
                world.Imports,
                imported: true)
            .AddRange(ResolveResourceFunctions(
                metadata,
                symbols,
                document,
                world.Imports,
                CanonicalAbiFunctionKind.ImportedResourceDrop))
            .AddRange(ResolveResourceFunctions(
                metadata,
                symbols,
                document,
                world.Exports,
                CanonicalAbiFunctionKind.ExportedResourceNew))
            .AddRange(ResolveResourceFunctions(
                metadata,
                symbols,
                document,
                world.Exports,
                CanonicalAbiFunctionKind.ExportedResourceRep))
            .AddRange(ResolveResourceFunctions(
                metadata,
                symbols,
                document,
                world.Exports,
                CanonicalAbiFunctionKind.ExportedResourceDrop));
        var exports = ResolveFunctions(
                metadata,
                symbols,
                document,
                world.Exports,
                imported: false)
            .AddRange(ResolveResourceFunctions(
                metadata,
                symbols,
                document,
                world.Exports,
                CanonicalAbiFunctionKind.ExportedResourceDestructor));
        return new(
            world.Package,
            world.Name,
            [.. imports.OrderBy(function => function.InterfaceName, StringComparer.Ordinal)
                .ThenBy(function => function.FunctionName, StringComparer.Ordinal)],
            [.. exports.OrderBy(function => function.InterfaceName, StringComparer.Ordinal)
                .ThenBy(function => function.FunctionName, StringComparer.Ordinal)]);
    }

    private ImmutableArray<CanonicalAbiFunction> ResolveResourceFunctions(
        MetadataCompilationSnapshot metadata,
        ISymbolFormatter symbols,
        WitDocument document,
        ImmutableArray<WitWorldItem> items,
        CanonicalAbiFunctionKind kind)
    {
        var functions = ImmutableArray.CreateBuilder<CanonicalAbiFunction>();
        foreach (var interfaceId in items
                     .Where(item => item.InterfaceId is not null)
                     .Select(item => item.InterfaceId!.Value)
                     .Distinct()
                     .Order())
        {
            var @interface = document.Interfaces[interfaceId];
            var interfaceName = $"{@interface.Package}/{@interface.Name}";
            foreach (var resource in @interface.Types
                         .OrderBy(pair => pair.Value)
                         .Select(pair => document.Types[pair.Value])
                         .Where(type => type.Kind.ValueKind == System.Text.Json.JsonValueKind.String &&
                             type.Kind.GetString() == "resource"))
            {
                functions.Add(ResolveResourceFunction(
                    metadata,
                    symbols,
                    document,
                    interfaceName,
                    resource.Name!,
                    kind));
            }
        }
        return functions.ToImmutable();
    }

    private CanonicalAbiFunction ResolveResourceFunction(
        MetadataCompilationSnapshot metadata,
        ISymbolFormatter symbols,
        WitDocument document,
        string interfaceName,
        string resourceName,
        CanonicalAbiFunctionKind kind)
    {
        var marker = string.Empty;
        switch (kind)
        {
            case CanonicalAbiFunctionKind.ImportedResourceDrop:
                marker = $"[resource-drop]{resourceName}";
                break;
            case CanonicalAbiFunctionKind.ExportedResourceNew:
                marker = $"[export-resource-new]{resourceName}";
                break;
            case CanonicalAbiFunctionKind.ExportedResourceRep:
                marker = $"[export-resource-rep]{resourceName}";
                break;
            case CanonicalAbiFunctionKind.ExportedResourceDrop:
                marker = $"[export-resource-drop]{resourceName}";
                break;
            case CanonicalAbiFunctionKind.ExportedResourceDestructor:
                marker = $"[resource-dtor]{resourceName}";
                break;
        }
        var imported = kind != CanonicalAbiFunctionKind.ExportedResourceDestructor;
        var matchingMethods = metadata.Methods.Where(method => imported
                ? method.WitImport?.InterfaceName == interfaceName &&
                    method.WitImport.FunctionName == marker
                : method.WitExport?.InterfaceName == interfaceName &&
                    method.WitExport.FunctionName == marker)
            .ToImmutableArray();
        var candidates = _bindings.Select(metadata, matchingMethods);
        if (!imported && candidates.Length != 1)
        {
            throw Invalid(
                $"WIT resource intrinsic '{Display(interfaceName, marker)}' requires exactly one matching managed binding");
        }
        var value = _canonicalTypes.Resolve(
            document,
            new WitTypeReference.Primitive("u32"));
        var result = kind is CanonicalAbiFunctionKind.ExportedResourceNew or
            CanonicalAbiFunctionKind.ExportedResourceRep
                ? value
                : null;
        var binding = new CanonicalAbiFunction(
            interfaceName,
            marker,
            candidates.IsEmpty ? UnboundImport : candidates[0].Key,
            [new CanonicalAbiParameter("handle", value)],
            result)
        {
            HasManagedBinding = !candidates.IsEmpty,
            Kind = kind,
            ResourceName = resourceName,
        };
        ValidateCanonicalSignatures(
            binding,
            imported ? matchingMethods : candidates,
            symbols,
            imported
                ? CanonicalAbiDirection.LoweredImport
                : CanonicalAbiDirection.LiftedExport);
        return binding;
    }

    private ImmutableArray<CanonicalAbiFunction> ResolveFunctions(
        MetadataCompilationSnapshot metadata,
        ISymbolFormatter symbols,
        WitDocument document,
        ImmutableArray<WitWorldItem> items,
        bool imported)
    {
        var functions = ImmutableArray.CreateBuilder<CanonicalAbiFunction>();
        foreach (var item in items)
        {
            if (item.Function is not null)
            {
                functions.Add(ResolveFunction(
                    metadata,
                    symbols,
                    document,
                    string.Empty,
                    item.Function,
                    imported));
                continue;
            }
            var @interface = document.Interfaces[item.InterfaceId!.Value];
            var interfaceName = $"{@interface.Package}/{@interface.Name}";
            foreach (var function in @interface.Functions)
            {
                functions.Add(ResolveFunction(
                    metadata,
                    symbols,
                    document,
                    interfaceName,
                    function,
                    imported));
            }
        }
        return functions
            .OrderBy(function => function.InterfaceName, StringComparer.Ordinal)
            .ThenBy(function => function.FunctionName, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private CanonicalAbiFunction ResolveFunction(
        MetadataCompilationSnapshot metadata,
        ISymbolFormatter symbols,
        WitDocument document,
        string interfaceName,
        WitFunction function,
        bool imported)
    {
        if (function.Kind.Name.StartsWith("async", StringComparison.Ordinal))
        {
            throw Invalid(
                $"user-defined asynchronous WIT function '{function.Name}' is not supported");
        }
        var matchingMethods = metadata.Methods.Where(method => imported
                ? method.WitImport?.InterfaceName == interfaceName &&
                    method.WitImport.FunctionName == function.Name
                : method.WitExport?.InterfaceName == interfaceName &&
                    method.WitExport.FunctionName == function.Name)
            .ToImmutableArray();
        var candidates = _bindings.Select(metadata, matchingMethods);
        if (!imported && candidates.Length != 1)
        {
            throw Invalid(
                $"WIT export '{Display(interfaceName, function.Name)}' requires exactly one matching managed binding");
        }
        var method = candidates.IsEmpty ? null : candidates[0];
        var parameters = function.Parameters.Select(parameter =>
            new CanonicalAbiParameter(
                parameter.Name,
                _canonicalTypes.Resolve(document, parameter.Type)))
            .ToImmutableArray();
        var result = function.Result is null
            ? null
            : _canonicalTypes.Resolve(document, function.Result);
        var binding = new CanonicalAbiFunction(
            interfaceName,
            function.Name,
            method?.Key ?? UnboundImport,
            parameters,
            result)
        {
            HasManagedBinding = method is not null,
        };
        ValidateCanonicalSignatures(
            binding,
            imported ? matchingMethods : candidates,
            symbols,
            imported
                ? CanonicalAbiDirection.LoweredImport
                : CanonicalAbiDirection.LiftedExport);
        if (!imported)
        {
            var postReturns = _bindings.Select(metadata,
                metadata.Methods.Where(candidate =>
                    candidate.WitPostReturn?.InterfaceName == interfaceName &&
                    candidate.WitPostReturn.FunctionName == function.Name));
            if (postReturns.Length > 1)
            {
                throw Invalid(
                    $"WIT export '{Display(interfaceName, function.Name)}' has multiple post-return bindings");
            }
            if (postReturns.Length == 1)
            {
                ValidatePostReturnSignature(
                    binding,
                    postReturns[0],
                    symbols.Format(postReturns[0]));
                binding = binding with { PostReturnMethod = postReturns[0].Key };
            }
        }
        return binding;
    }

    private void ValidateCanonicalSignatures(
        CanonicalAbiFunction function,
        ImmutableArray<MethodDefinitionModel> methods,
        ISymbolFormatter symbols,
        CanonicalAbiDirection direction)
    {
        foreach (var method in methods)
        {
            ValidateCanonicalSignature(
                function,
                method,
                symbols.Format(method),
                direction);
        }
    }

    private void ValidateCanonicalSignature(
        CanonicalAbiFunction function,
        MethodDefinitionModel method,
        string methodName,
        CanonicalAbiDirection direction)
    {
        var expected = _signatures.Plan(function, direction);
        var parameters = expected.Parameters.Select(NormalizeAddress).ToArray();
        var result = NormalizeAddress(expected.Result);
        if (!method.Signature.ParameterTypes.AsSpan().SequenceEqual(parameters) ||
            method.Signature.ReturnType != result)
        {
            throw Invalid(
                $"WIT function '{Display(function.InterfaceName, function.FunctionName)}' requires canonical core signature ({string.Join(", ", parameters)}) -> {result}",
                methodName);
        }
    }

    private void ValidatePostReturnSignature(
        CanonicalAbiFunction function,
        MethodDefinitionModel method,
        string methodName)
    {
        var expected = CanonicalAbiSignaturePlanner
            .PostReturnParameters(_signatures.Plan(
                function,
                CanonicalAbiDirection.LiftedExport))
            .Select(NormalizeAddress)
            .ToArray();
        if (!method.Signature.ParameterTypes.AsSpan().SequenceEqual(expected) ||
            method.Signature.ReturnType != CliValueKind.Void)
        {
            throw Invalid(
                $"WIT post-return '{Display(function.InterfaceName, function.FunctionName)}' requires canonical core signature ({string.Join(", ", expected)}) -> Void",
                methodName);
        }
    }

    private static CliValueKind NormalizeAddress(CliValueKind kind) =>
        kind == CliValueKind.ManagedAddress ? CliValueKind.NativeInt : kind;

    private static string Display(string interfaceName, string functionName) =>
        interfaceName.Length == 0 ? functionName : $"{interfaceName}#{functionName}";

    private static CompilerException Invalid(string message, string? method = null) =>
        new(new CompilerDiagnostic(DiagnosticCode.ComponentContract, message, method));
}
