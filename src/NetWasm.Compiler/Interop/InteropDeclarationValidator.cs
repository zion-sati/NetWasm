// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Interop;

internal interface IInteropDeclarationValidator
{
    void Validate(
        MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver typeDefinitions,
        IBaseTypeResolver baseTypes,
        IMethodRepository methods,
        ISymbolFormatter symbols);
}

internal sealed class InteropDeclarationValidator(
    IDelegateTypeRecognizerFactory delegateTypes) : IInteropDeclarationValidator
{
    private readonly IDelegateTypeRecognizerFactory _delegateTypes = delegateTypes ??
        throw new ArgumentNullException(nameof(delegateTypes));

    public void Validate(
        MetadataCompilationSnapshot metadata,
        ITypeDefinitionResolver typeDefinitions,
        IBaseTypeResolver baseTypes,
        IMethodRepository methods,
        ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(typeDefinitions);
        ArgumentNullException.ThrowIfNull(baseTypes);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(symbols);
        var delegateTypes = _delegateTypes.Create(typeDefinitions, baseTypes);
        // JSExports from every assembly share one module namespace.
        var exportNames = new Dictionary<string, MethodDefinitionModel>(StringComparer.Ordinal);
        foreach (var method in metadata.Methods
                     .Where(method => method.JSImport is not null || method.JSExport is not null ||
                         method.WitImport is not null || method.WitExport is not null ||
                         method.WitPostReturn is not null))
        {
            if (method.JSImport is not null)
            {
                ValidateImport(delegateTypes, typeDefinitions, methods, symbols, method);
            }
            if (method.JSExport is not null)
            {
                var exportName = method.JSExport.ExportName ?? method.Name;
                if (!exportNames.TryAdd(exportName, method))
                {
                    var first = exportNames[exportName];
                    throw Invalid(symbols, method,
                        $"duplicate JSExport name '{exportName}': declared by " +
                        $"{symbols.Format(first)} in {first.Key.Assembly.Name} and " +
                        $"{symbols.Format(method)} in {method.Key.Assembly.Name}");
                }
                ValidateExport(symbols, method);
            }
            if (method.WitImport is not null)
            {
                ValidateWitImport(symbols, method);
            }
            if (method.WitExport is not null)
            {
                ValidateWitExport(symbols, method);
            }
            if (method.WitPostReturn is not null && (!method.IsStatic || !method.HasBody))
            {
                throw Invalid(symbols, method,
                    "WitPostReturn must be a static method with a CIL body");
            }
        }
    }

    private static void ValidateWitImport(
        ISymbolFormatter symbols,
        MethodDefinitionModel method)
    {
        if (!method.IsStatic || method.HasBody)
        {
            throw Invalid(symbols, method,
                "WitImport must be a bodyless static method");
        }
        ValidateScalarSignature(symbols, method, "WitImport");
        if (method.WitImport!.IsCanonicalBuiltIn)
        {
            ValidateComponentBuiltIn(symbols, method, method.WitImport);
        }
    }

    // Built-ins are bracketed core names in `$root`, an `[export]` module, or an
    // interface at its full version; anything else is a WIT function.
    private static void ValidateComponentBuiltIn(
        ISymbolFormatter symbols,
        MethodDefinitionModel method,
        WitImportDeclaration builtIn)
    {
        var module = builtIn.InterfaceName;
        var interfaceName = module.StartsWith("[export]", StringComparison.Ordinal)
            ? module["[export]".Length..]
            : module;
        // `[export]$root` is the module of a world function's task.return.
        var moduleIsValid = interfaceName == "$root" ||
            (interfaceName.Contains('/', StringComparison.Ordinal) &&
                interfaceName.IndexOf('@', StringComparison.Ordinal) is var version and > 0 &&
                interfaceName[(version + 1)..].Split('.').Length >= 3);
        if (!moduleIsValid || !builtIn.FunctionName.StartsWith('['))
        {
            throw Invalid(symbols, method,
                "ComponentBuiltIn must name '$root', '[export]' and the world or an interface, or an interface " +
                "at its full version, and a bracketed built-in name");
        }
    }

    private static void ValidateWitExport(
        ISymbolFormatter symbols,
        MethodDefinitionModel method)
    {
        if (!method.IsStatic || !method.HasBody)
        {
            throw Invalid(symbols, method,
                "WitExport must be a static method with a CIL body");
        }
        ValidateScalarSignature(symbols, method, "WitExport");
    }

    private const string GlobalPrefix = "globalThis.";

    private static void ValidateImport(
        IDelegateTypeRecognizer delegateTypes,
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        ISymbolFormatter symbols,
        MethodDefinitionModel method)
    {
        if (!method.IsStatic || method.HasBody)
        {
            throw Invalid(symbols, method,
                "JSImport must be a bodyless static method");
        }
        // As on .NET's browser runtime, an import without a module calls a global function, "globalThis.<path>";
        // any other names the module it comes from.
        if (method.JSImport!.ModuleName is null &&
            !(method.JSImport.FunctionName.StartsWith(GlobalPrefix, StringComparison.Ordinal) &&
              method.JSImport.FunctionName.Length > GlobalPrefix.Length))
        {
            throw Invalid(symbols, method,
                "JSImport without a module name must name a global function, as \"globalThis.<path>\"; any other import names its module");
        }
        ValidateImportSignature(
            delegateTypes,
            typeDefinitions,
            methods,
            symbols,
            method);
    }

    private static void ValidateExport(
        ISymbolFormatter symbols,
        MethodDefinitionModel method)
    {
        if (!method.IsStatic || !method.HasBody)
        {
            throw Invalid(symbols, method,
                "JSExport must be a static method with a CIL body");
        }
        if (JavaScriptBoundaryShapes.UnsupportedExport(method.Signature, method.JSExport!.MarshalAs) is { } reason)
        {
            throw Invalid(symbols, method, reason);
        }
    }

    private static void ValidateImportSignature(
        IDelegateTypeRecognizer delegateTypes,
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        ISymbolFormatter symbols,
        MethodDefinitionModel method)
    {
        if (JavaScriptBoundaryShapes.UnsupportedImport(method.Signature, method.JSImport!.MarshalAs,
                method.JSImport.IsPromise, type => delegateTypes.Recognize(type) ? GetInvokeSignature(type) : null) is
            { } reason)
        {
            throw Invalid(symbols, method, reason);
        }

        MethodSignatureModel GetInvokeSignature(CliTypeIdentity callback)
        {
            var invoke = typeDefinitions.ResolveTypeIdentity(callback).Methods
                .Select(methods.GetMethod)
                .SingleOrDefault(candidate => candidate.Name == "Invoke")
                ?? throw Invalid(symbols, method,
                    "JSImport callback delegate has no Invoke method");
            return invoke.Signature.Substitute(
                callback.Shape == CliTypeShape.GenericInstantiation
                    ? callback.TypeArguments
                    : []);
        }
    }

    private static void ValidateScalarSignature(
        ISymbolFormatter symbols,
        MethodDefinitionModel method,
        string declaration)
    {
        if (method.Signature.ReturnType != CliValueKind.Void &&
            !IsHostScalar(method.Signature.ReturnSignatureType) ||
            method.Signature.ParameterSignatureTypes.Any(type => !IsHostScalar(type)))
        {
            throw Invalid(symbols, method,
                $"{declaration} currently supports only primitive scalar parameters and primitive scalar or void results");
        }
    }

    private static bool IsHostScalar(CliTypeIdentity type) => JavaScriptBoundaryShapes.IsHostScalar(type);

    private static CompilerException Invalid(
        ISymbolFormatter symbols,
        MethodDefinitionModel method,
        string message) => new(new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            message,
            symbols.Format(method)));
}
