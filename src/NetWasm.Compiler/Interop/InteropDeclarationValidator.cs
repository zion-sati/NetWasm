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
        if (method.JSImport!.ModuleName is null)
        {
            throw Invalid(symbols, method,
                "JSImport currently requires an explicit consumer module name");
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
        var asyncReturn = JavaScriptAsyncSignature.Classify(
            method.Signature.ReturnSignatureType);
        if (!asyncReturn.IsAsync)
        {
            if (method.Signature.ParameterSignatureTypes.Any(type =>
                    !IsHostScalar(type) && !IsExportReference(type)) ||
                method.Signature.ReturnType != CliValueKind.Void &&
                !IsHostScalar(method.Signature.ReturnSignatureType) &&
                !IsExportReference(method.Signature.ReturnSignatureType))
            {
                throw Invalid(symbols, method,
                    "JSExport currently supports primitive scalars, string, primitive number arrays such as byte[] or double[], JSObject, or void results and primitive scalar, string, primitive number array, or JSObject parameters");
            }
            return;
        }
        ValidateAsyncResult(symbols, method, asyncReturn, "JSExport");
        if (method.Signature.ParameterSignatureTypes.Any(type =>
                !IsHostScalar(type) && !IsExportReference(type)))
        {
            throw Invalid(symbols, method,
                "asynchronous JSExport currently supports primitive scalar, string, primitive number array, or JSObject parameters");
        }
    }

    private static void ValidateImportSignature(
        IDelegateTypeRecognizer delegateTypes,
        ITypeDefinitionResolver typeDefinitions,
        IMethodRepository methods,
        ISymbolFormatter symbols,
        MethodDefinitionModel method)
    {
        var asyncReturn = JavaScriptAsyncSignature.Classify(
            method.Signature.ReturnSignatureType);
        if ((!asyncReturn.IsAsync && method.Signature.ReturnType != CliValueKind.Void &&
            !IsHostValue(method.Signature.ReturnSignatureType)) ||
            method.Signature.ParameterSignatureTypes.Any(type =>
                !IsHostScalar(type) && !IsString(type) && !IsValueArray(type) &&
                !IsMemoryView(type) && !IsHostObject(type) && !delegateTypes.Recognize(type)))
        {
            throw Invalid(symbols, method,
                "JSImport currently supports primitive scalars, string, primitive number arrays such as byte[] or double[], Span<T> or ReadOnlySpan<T> of a primitive number, JSObject, and supported delegate parameters and primitive scalars, string, primitive number arrays, JSObject, JSSubscription, or void results");
        }
        // A span reaches JavaScript as a view that is valid only until the call returns.
        if ((asyncReturn.IsAsync || method.JSImport!.IsPromise) &&
            method.Signature.ParameterSignatureTypes.Any(IsMemoryView))
        {
            throw Invalid(symbols, method,
                "JSImport Span<T> and ReadOnlySpan<T> parameters are views valid only during the call, so an asynchronous JSImport cannot take them; pass a number array instead");
        }
        if (asyncReturn.IsAsync)
        {
            if (method.JSImport!.IsPromise)
            {
                throw Invalid(symbols, method,
                    "JSImportPromise is the callback-compatible lowered form; use ordinary [JSImport] for Task or ValueTask results");
            }
            ValidateAsyncResult(symbols, method, asyncReturn, "JSImport");
        }
        var callbacks = method.Signature.ParameterSignatureTypes
            .Where(delegateTypes.Recognize)
            .ToArray();
        if (method.JSImport!.IsPromise)
        {
            if (callbacks.Length != 2 ||
                !IsJSSubscription(method.Signature.ReturnSignatureType) ||
                !method.Signature.ParameterSignatureTypes.TakeLast(2)
                    .All(delegateTypes.Recognize))
            {
                throw Invalid(symbols, method,
                    "JSImportPromise requires success and failure delegate parameters at the end of the signature and a JSSubscription result");
            }
            var success = GetInvokeSignature(callbacks[0]);
            var failure = GetInvokeSignature(callbacks[1]);
            if (success.ReturnType != CliValueKind.Void ||
                success.ParameterSignatureTypes.Length != 1 ||
                failure.ReturnType != CliValueKind.Void ||
                !failure.ParameterSignatureTypes.IsEmpty)
            {
                throw Invalid(symbols, method,
                    "JSImportPromise requires Action<T> success and Action failure callbacks");
            }
        }
        if (callbacks.Length == 0)
        {
            return;
        }
        if (!method.JSImport.IsPromise &&
            (callbacks.Length != 1 || !IsJSSubscription(method.Signature.ReturnSignatureType)))
        {
            throw Invalid(symbols, method,
                "JSImport callbacks require exactly one delegate parameter and a JSSubscription result");
        }
        foreach (var callback in callbacks)
        {
            var invoke = GetInvokeSignature(callback);
            if (invoke.ReturnType != CliValueKind.Void &&
                !IsHostScalar(invoke.ReturnSignatureType) ||
                invoke.ParameterSignatureTypes.Any(type =>
                    !IsHostScalar(type) && !IsString(type) && !IsValueArray(type)) ||
                invoke.ParameterSignatureTypes.Count(type =>
                    IsString(type) || IsValueArray(type)) > 1)
            {
                throw Invalid(symbols, method,
                    "JSImport callbacks support primitive scalar parameters and at most one string or primitive number array parameter, with primitive scalar or void results");
            }
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

    private static bool IsString(CliTypeIdentity type) =>
        type.CanonicalName == "primitive:string";

    private static bool IsHostScalar(CliTypeIdentity type) =>
        type.StackKind is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or
            CliValueKind.NativeInt;

    private static bool IsHostValue(CliTypeIdentity type) =>
        IsHostScalar(type) || IsString(type) || IsValueArray(type) || IsHostObject(type);

    private static void ValidateAsyncResult(
        ISymbolFormatter symbols,
        MethodDefinitionModel method,
        JavaScriptAsyncReturn asyncReturn,
        string declaration)
    {
        if (asyncReturn.ResultType is not { } result || IsHostScalar(result))
        {
            return;
        }
        // An import's settled value is copied (string, arrays) or wrapped (JSObject)
        // as it arrives; an export result is read once by the host and stays scalar.
        if (declaration == "JSImport" && (IsString(result) || IsValueArray(result) || IsJSObject(result)))
        {
            return;
        }
        throw Invalid(symbols, method, declaration == "JSImport"
            ? "JSImport Task and ValueTask results currently support primitive scalars, string, primitive number arrays, or JSObject"
            : "JSExport Task and ValueTask results currently support primitive scalars");
    }

    private static bool IsValueArray(CliTypeIdentity type) => JavaScriptValueArray.IsSupported(type);

    private static bool IsMemoryView(CliTypeIdentity type) => JavaScriptMemoryView.IsSupported(type);

    private static bool IsExportReference(CliTypeIdentity type) =>
        IsString(type) || IsValueArray(type) || IsJSObject(type);

    private static bool IsJSObject(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSObject";

    private static bool IsHostObject(CliTypeIdentity type) =>
        IsJSObject(type) || IsJSSubscription(type);

    private static bool IsJSSubscription(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSSubscription";

    private static CompilerException Invalid(
        ISymbolFormatter symbols,
        MethodDefinitionModel method,
        string message) => new(new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            message,
            symbols.Format(method)));
}
