using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record InteropImportPlan(
    ImmutableArray<WasmFunctionImport> Imports,
    OptionalFunctionIndex StringLength,
    OptionalFunctionIndex CopyStringUtf16,
    OptionalFunctionIndex ReleaseHandle,
    OptionalFunctionIndex ReleaseSubscription,
    OptionalFunctionIndex ByteLength,
    OptionalFunctionIndex CopyBytes,
    bool UseJavaScriptExportBoundary = false)
{
    // The Wasm functions of CoreLib's conversions (JavaScriptConversion), by name,
    // for the values reachable boundaries convert. A date's take or leave Unix
    // milliseconds with the struct at an address, as any value-type argument or
    // result is.
    public ImmutableDictionary<string, int> Conversions { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    /// <summary>The function that takes a managed value of the type to JavaScript.</summary>
    public int FromManaged(CliTypeIdentity type) => Require(JavaScriptConversion.FromManaged(type), type);

    /// <summary>The function that brings a JavaScript value into managed code as the type.</summary>
    public int ToManaged(CliTypeIdentity type) => Require(JavaScriptConversion.ToManaged(type), type);

    private int Require(string? name, CliTypeIdentity type) =>
        name is not null && Conversions.TryGetValue(name, out var function)
            ? function
            : throw new InvalidOperationException(
                $"no reachable JavaScript boundary converts '{type.CanonicalName}' that way");
}

internal interface IInteropImportPlanner
{
    InteropImportPlan Build(WasmEmissionRequest request, int firstIndex);
}

internal sealed class InteropImportPlanner : IInteropImportPlanner
{
    public InteropImportPlan Build(WasmEmissionRequest request, int firstIndex)
    {
        var useJavaScriptExportBoundary =
            request.UseJavaScriptExportBoundary ||
            request.ModuleProfile == WasmModuleProfile.CoreApplication;
        var requestedExports = RequestedExportMethodSelector.Select(
            request.RequestedExports,
            request.MethodInstances);
        var hasStrings = request.JSImportMethods.Any(method =>
            InteropTypeClassifier.IsString(ImportResult(method))) ||
            request.HostCallbacks.Any(callback => callback.Invoke.Signature
                .ParameterSignatureTypes.Any(InteropTypeClassifier.IsString)) ||
            useJavaScriptExportBoundary &&
            requestedExports.Values.Any(method =>
                method.Signature.ParameterSignatureTypes.Any(
                    InteropTypeClassifier.IsString) ||
                InteropTypeClassifier.IsString(
                    method.Signature.ReturnSignatureType));
        var hasBytes = request.JSImportMethods.Any(method =>
            InteropTypeClassifier.IsValueArray(ImportResult(method))) ||
            request.HostCallbacks.Any(callback => callback.Invoke.Signature
                .ParameterSignatureTypes.Any(InteropTypeClassifier.IsValueArray)) ||
            useJavaScriptExportBoundary &&
            requestedExports.Values.Any(method =>
                method.Signature.ParameterSignatureTypes.Any(
                    InteropTypeClassifier.IsValueArray) ||
                InteropTypeClassifier.IsValueArray(
                    method.Signature.ReturnSignatureType));
        // A handle value's import result handle is released once CoreLib converts it.
        var hasObjects = request.JSImportMethods.Any(method =>
            InteropTypeClassifier.IsHostObject(ImportResult(method)) ||
            InteropTypeClassifier.IsHandleValue(ImportResult(method)) ||
            method.Signature.ParameterSignatureTypes.Any(InteropTypeClassifier.IsHostObject)) ||
            useJavaScriptExportBoundary &&
            requestedExports.Values.Any(method =>
                method.Signature.ParameterSignatureTypes.Any(InteropTypeClassifier.IsJSObject) ||
                InteropTypeClassifier.IsJSObject(method.Signature.ReturnSignatureType));
        var hasSubscriptions = request.JSImportMethods.Any(method =>
            InteropTypeClassifier.IsSubscription(method.Signature.ReturnSignatureType));
        var imports = ImmutableArray.CreateBuilder<WasmFunctionImport>();

        var stringLength = Append(
            imports,
            hasStrings,
            firstIndex,
            WasmRuntimeImports.CreateHostInteropStringResultImports(),
            out var copyString);
        var byteLength = Append(
            imports,
            hasBytes,
            firstIndex,
            WasmRuntimeImports.CreateHostInteropByteResultImports(),
            out var copyBytes);
        var releaseHandle = AppendSingle(
            imports,
            hasStrings || hasBytes || hasObjects,
            firstIndex,
            WasmRuntimeImports.CreateHostInteropHandleImports());
        var releaseSubscription = AppendSingle(
            imports,
            hasSubscriptions,
            firstIndex,
            WasmRuntimeImports.CreateHostInteropSubscriptionImports());

        return new(
            imports.ToImmutable(),
            stringLength,
            copyString,
            releaseHandle,
            releaseSubscription,
            byteLength,
            copyBytes,
            useJavaScriptExportBoundary);
    }

    // An async import settles with the Task's result, which marshals like a synchronous one.
    private static CliTypeIdentity ImportResult(MethodDefinitionModel method) =>
        JavaScriptAsyncSignature.Classify(method.Signature.ReturnSignatureType).ResultType
            ?? method.Signature.ReturnSignatureType;

    private static OptionalFunctionIndex Append(
        ImmutableArray<WasmFunctionImport>.Builder destination,
        bool required,
        int firstIndex,
        IReadOnlyList<WasmFunctionImport> imports,
        out OptionalFunctionIndex second)
    {
        if (!required)
        {
            second = OptionalFunctionIndex.Missing;
            return OptionalFunctionIndex.Missing;
        }

        var first = OptionalFunctionIndex.At(firstIndex + destination.Count);
        destination.AddRange(imports);
        second = OptionalFunctionIndex.At(first.Value + 1);
        return first;
    }

    private static OptionalFunctionIndex AppendSingle(
        ImmutableArray<WasmFunctionImport>.Builder destination,
        bool required,
        int firstIndex,
        IReadOnlyList<WasmFunctionImport> imports)
    {
        if (!required)
        {
            return OptionalFunctionIndex.Missing;
        }

        var index = OptionalFunctionIndex.At(firstIndex + destination.Count);
        destination.AddRange(imports);
        return index;
    }
}
