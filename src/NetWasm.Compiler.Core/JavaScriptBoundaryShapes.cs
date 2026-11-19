// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;

namespace NetWasm.Compiler.Core;

// The values a JSImport or JSExport passes. A JavaScript boundary passes primitive scalars, dates and their nullables
// by value, and strings, number arrays, JSObject and the objects, exceptions and reference arrays CoreLib converts as
// host handles. Each check answers why a signature cannot cross, or null when it can, so the compiler's validator and
// the metadata reader, which decides whether a library's generated binding is NetWasm interop, apply one rule.
public static class JavaScriptBoundaryShapes
{
    public static bool IsHostScalar(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.StackKind is CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8 or
            CliValueKind.NativeInt;
    }

    public static bool IsString(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.CanonicalName == "primitive:string";
    }

    // A JavaScript boundary also passes a date, as a Date, and either as nullable.
    public static bool IsJavaScriptScalar(CliTypeIdentity type) =>
        IsHostScalar(type) || JavaScriptConversion.IsDate(type) || JavaScriptNullable.IsSupported(type);

    public static bool IsValueArray(CliTypeIdentity type) => JavaScriptValueArray.IsSupported(type);

    public static bool IsMemoryView(CliTypeIdentity type) => JavaScriptMemoryView.IsSupported(type);

    public static bool IsJSObject(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.FullName == "System.Runtime.InteropServices.JavaScript.JSObject";
    }

    public static bool IsJSSubscription(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.FullName == "System.Runtime.InteropServices.JavaScript.JSSubscription";
    }

    public static bool IsHostObject(CliTypeIdentity type) => IsJSObject(type) || IsJSSubscription(type);

    public static bool IsReturnedHandleValue(CliTypeIdentity type) =>
        JavaScriptConversion.IsHandleValue(type) && !JavaScriptArraySegment.IsSupported(type);

    // A value an import returns. An ArraySegment's view only goes to JavaScript.
    public static bool IsHostValue(CliTypeIdentity type) =>
        IsJavaScriptScalar(type) || IsString(type) || IsValueArray(type) || IsHostObject(type) ||
        IsReturnedHandleValue(type);

    // A value that crosses as a host handle: a string, number array or JSObject, or an object, exception or array of
    // references that CoreLib converts.
    public static bool IsExportReference(CliTypeIdentity type) =>
        IsString(type) || IsValueArray(type) || IsJSObject(type) || IsReturnedHandleValue(type);

    // A settled value takes the types a synchronous call returns: an import's is copied (string, arrays) or wrapped
    // (JSObject) as it arrives, and the host reads an export's from the completed task as it reads a synchronous
    // result.
    public static string? UnsupportedAsyncResult(JavaScriptAsyncReturn asyncReturn, string declaration)
    {
        return asyncReturn.ResultType is not { } result || IsJavaScriptScalar(result) || IsString(result) ||
               IsValueArray(result) || IsJSObject(result) || IsReturnedHandleValue(result)
            ? null
            : $"{declaration} Task and ValueTask results currently support primitive scalars, DateTime, DateTimeOffset, their nullables, string, object, Exception, primitive number arrays, string[], JSObject[], object[], or JSObject";
    }

    // Why a [JSMarshalAs<T>] marker does not fit its parameter or result, or null when every one fits. Invoke gives a
    // delegate's Invoke signature, or null for a type that is no delegate.
    public static string? UnsupportedMarshalAs(MethodSignatureModel signature, JavaScriptMarshalAs marshalAs,
        Func<CliTypeIdentity, MethodSignatureModel?> invoke)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(marshalAs);
        ArgumentNullException.ThrowIfNull(invoke);
        for (var index = 0; index < signature.ParameterSignatureTypes.Length; index++)
        {
            if (Check(signature.ParameterSignatureTypes[index], marshalAs.Parameter(index), $"parameter {index + 1}") is
                { } parameter)
                return parameter;
        }
        return Check(signature.ReturnSignatureType, marshalAs.Result, "result");

        string? Check(CliTypeIdentity type, string? marker, string position) =>
            marker is not null && JavaScriptMarshalAsRules.Mismatch(type, marker, invoke) is { } expected
                ? $"JSMarshalAs<{JavaScriptMarshalAsRules.Describe(marker)}> does not fit the {position}, which takes " +
                  JavaScriptMarshalAsRules.Describe(expected)
                : null;
    }

    // Why a JSExport's signature cannot cross the boundary, or null when it can. An export takes no delegate.
    public static string? UnsupportedExport(MethodSignatureModel signature, JavaScriptMarshalAs marshalAs)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(marshalAs);
        var asyncReturn = JavaScriptAsyncSignature.Classify(signature.ReturnSignatureType);
        if (!asyncReturn.IsAsync)
        {
            if (signature.ParameterSignatureTypes.Any(type => !IsJavaScriptScalar(type) && !IsExportReference(type)) ||
                signature.ReturnType != CliValueKind.Void && !IsJavaScriptScalar(signature.ReturnSignatureType) &&
                !IsExportReference(signature.ReturnSignatureType))
            {
                return "JSExport currently supports primitive scalars, DateTime, DateTimeOffset, their nullables, string, object, Exception, primitive number arrays such as byte[] or double[], string[], JSObject[], object[], JSObject, or void results and the same parameters";
            }
        }
        else
        {
            if (UnsupportedAsyncResult(asyncReturn, "JSExport") is { } result) return result;
            if (signature.ParameterSignatureTypes.Any(type => !IsJavaScriptScalar(type) && !IsExportReference(type)))
            {
                return "asynchronous JSExport currently supports primitive scalar, DateTime, DateTimeOffset, nullable, string, object, Exception, primitive number array, string[], JSObject[], object[], or JSObject parameters";
            }
        }
        return UnsupportedMarshalAs(signature, marshalAs, _ => null);
    }

    // Why a JSImport's signature cannot cross the boundary, or null when it can. Invoke gives a delegate parameter's
    // Invoke signature, or null for a type that is no delegate.
    public static string? UnsupportedImport(MethodSignatureModel signature, JavaScriptMarshalAs marshalAs, bool isPromise,
        Func<CliTypeIdentity, MethodSignatureModel?> invoke)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(marshalAs);
        ArgumentNullException.ThrowIfNull(invoke);
        var asyncReturn = JavaScriptAsyncSignature.Classify(signature.ReturnSignatureType);
        if (!asyncReturn.IsAsync && signature.ReturnType != CliValueKind.Void && !IsHostValue(signature.ReturnSignatureType) ||
            signature.ParameterSignatureTypes.Any(type =>
                !IsJavaScriptScalar(type) && !IsString(type) && !IsValueArray(type) && !IsMemoryView(type) &&
                !IsHostObject(type) && !JavaScriptConversion.IsHandleValue(type) && invoke(type) is null))
        {
            return "JSImport currently supports primitive scalars, DateTime, DateTimeOffset, their nullables, string, object, Exception, primitive number arrays such as byte[] or double[], string[], JSObject[], object[], Span<T>, ReadOnlySpan<T> or ArraySegment<T> of a primitive number, JSObject, and supported delegate parameters and the same values except spans and ArraySegment<T>, JSSubscription, or void results";
        }
        if (UnsupportedMarshalAs(signature, marshalAs, invoke) is { } marker) return marker;
        // A span reaches JavaScript as a view that is valid only until the call returns.
        if ((asyncReturn.IsAsync || isPromise) && signature.ParameterSignatureTypes.Any(IsMemoryView))
        {
            return "JSImport Span<T> and ReadOnlySpan<T> parameters are views valid only during the call, so an asynchronous JSImport cannot take them; pass a number array instead";
        }
        if (asyncReturn.IsAsync)
        {
            if (isPromise)
            {
                return "JSImportPromise is the callback-compatible lowered form; use ordinary [JSImport] for Task or ValueTask results";
            }
            if (UnsupportedAsyncResult(asyncReturn, "JSImport") is { } result) return result;
        }
        var callbacks = signature.ParameterSignatureTypes.Select(type => invoke(type)).OfType<MethodSignatureModel>().ToArray();
        if (isPromise)
        {
            if (callbacks.Length != 2 || !IsJSSubscription(signature.ReturnSignatureType) ||
                !signature.ParameterSignatureTypes.TakeLast(2).All(type => invoke(type) is not null))
            {
                return "JSImportPromise requires success and failure delegate parameters at the end of the signature and a JSSubscription result";
            }
            if (callbacks[0].ReturnType != CliValueKind.Void || callbacks[0].ParameterSignatureTypes.Length != 1 ||
                callbacks[1].ReturnType != CliValueKind.Void || !callbacks[1].ParameterSignatureTypes.IsEmpty)
            {
                return "JSImportPromise requires Action<T> success and Action failure callbacks";
            }
        }
        if (callbacks.Length == 0) return null;
        // A JSSubscription result releases the one callback it owns when disposed. With any other result the
        // callbacks belong to JavaScript, which keeps each until it drops the function, as on .NET.
        if (!isPromise && callbacks.Length != 1 && IsJSSubscription(signature.ReturnSignatureType))
        {
            return "a JSImport returning JSSubscription takes exactly one delegate parameter, which disposing the subscription releases";
        }
        // A callback is entered as a synchronous export is, with the same values.
        return callbacks.Any(callback =>
                callback.ReturnType != CliValueKind.Void && !IsJavaScriptScalar(callback.ReturnSignatureType) &&
                !IsExportReference(callback.ReturnSignatureType) ||
                callback.ParameterSignatureTypes.Any(type => !IsJavaScriptScalar(type) && !IsExportReference(type)))
            ? "JSImport callbacks support primitive scalar, DateTime, DateTimeOffset, nullable, string, object, Exception, primitive number array, string[], JSObject[], object[], or JSObject parameters and the same results or void, as a synchronous JSExport does"
            : null;
    }

    // The Invoke signature of System.Action and System.Func, the delegates .NET's JavaScript binding generator accepts,
    // read from their type arguments; null for any other type.
    public static MethodSignatureModel? StandardDelegateInvoke(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.FullName == "System.Action" && type.Shape == CliTypeShape.Named)
            return new(CliValueKind.Void, []);
        if (type.Shape != CliTypeShape.GenericInstantiation)
            return null;
        var definition = type.ElementType!.FullName;
        var arguments = type.TypeArguments;
        if (definition == $"System.Action`{arguments.Length}")
            return new(CliTypeIdentity.FromStackKind(CliValueKind.Void), arguments);
        // A Func has its result last; no Func`0 exists.
        if (definition == $"System.Func`{arguments.Length}")
            return new(arguments[^1], arguments[..^1]);
        return null;
    }
}
