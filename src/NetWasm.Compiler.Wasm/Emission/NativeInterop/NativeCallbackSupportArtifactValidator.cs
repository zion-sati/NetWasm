using System;
using System.Linq;
using System.Security.Cryptography;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Emission.NativeInterop;

public sealed class NativeCallbackSupportArtifactValidator :
    INativeCallbackSupportArtifactValidator
{
    public void Validate(WasmNativeCallbackSupportArtifact? support)
    {
        if (support is null)
        {
            return;
        }
        if (support.ObjectBytes is null || support.ObjectBytes.Length == 0 ||
            string.IsNullOrWhiteSpace(support.Sha256) ||
            support.Callbacks.IsDefaultOrEmpty ||
            support.TemporaryApplicationExports.IsDefault ||
            support.TemporaryRuntimeExports.IsDefault ||
            support.Callbacks.Any(InvalidCallback) ||
            HasDuplicateSymbols(support) ||
            !support.TemporaryApplicationExports.SequenceEqual(
                support.Callbacks
                    .Where(callback => callback.NativeSymbol !=
                        callback.ApplicationExportName)
                    .Select(callback => callback.ApplicationExportName),
                StringComparer.Ordinal) ||
            !support.TemporaryRuntimeExports.SequenceEqual(
                support.Callbacks
                    .Where(callback => callback.RuntimeGetterExportName is not null)
                    .Select(callback => callback.RuntimeGetterExportName!),
                StringComparer.Ordinal) ||
            support.TemporaryApplicationExports.Distinct(StringComparer.Ordinal).Count() !=
                support.TemporaryApplicationExports.Length ||
            support.TemporaryRuntimeExports.Distinct(StringComparer.Ordinal).Count() !=
                support.TemporaryRuntimeExports.Length)
        {
            throw InvalidArtifact();
        }

        var digest = Convert.ToHexString(SHA256.HashData(support.ObjectBytes))
            .ToLowerInvariant();
        if (!string.Equals(digest, support.Sha256, StringComparison.Ordinal))
        {
            throw InvalidArtifact();
        }
    }

    private static bool InvalidCallback(WasmNativeCallbackDescriptor? callback) =>
        callback is null ||
        InvalidName(callback.NativeSymbol) ||
        InvalidName(callback.RuntimeImportSymbol) ||
        InvalidName(callback.ApplicationExportName) ||
        callback.RuntimeGetterExportName is { } getter && InvalidName(getter) ||
        callback.Parameters.IsDefault ||
        callback.Parameters.Any(static parameter => !Enum.IsDefined(parameter)) ||
        callback.ReturnType is { } returnType && !Enum.IsDefined(returnType);

    private static bool InvalidName(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.IndexOfAny(['\0', '\r', '\n']) >= 0;

    private static bool HasDuplicateSymbols(
        WasmNativeCallbackSupportArtifact support)
    {
        var applicationExports = support.Callbacks
            .Select(callback => callback.ApplicationExportName)
            .ToArray();
        var linkerSymbols = support.Callbacks
            .Select(callback => callback.RuntimeImportSymbol)
            .Concat(support.Callbacks
                .Where(callback => !string.Equals(
                    callback.NativeSymbol,
                    callback.RuntimeImportSymbol,
                    StringComparison.Ordinal))
                .Select(callback => callback.NativeSymbol))
            .Concat(support.Callbacks
                .Where(callback => callback.RuntimeGetterExportName is not null)
                .Select(callback => callback.RuntimeGetterExportName!))
            .ToArray();
        return applicationExports.Distinct(StringComparer.Ordinal).Count() !=
                applicationExports.Length ||
            linkerSymbols.Distinct(StringComparer.Ordinal).Count() !=
                linkerSymbols.Length;
    }

    private static InvalidOperationException InvalidArtifact() => new(
        "The compiler native callback support artifact is invalid.");
}
