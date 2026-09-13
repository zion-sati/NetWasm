using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record InteropImportPlan(
    ImmutableArray<WasmFunctionImport> Imports,
    OptionalFunctionIndex StringLength,
    OptionalFunctionIndex CopyStringUtf16,
    OptionalFunctionIndex ReleaseHandle,
    OptionalFunctionIndex ReleaseSubscription,
    OptionalFunctionIndex ByteLength,
    OptionalFunctionIndex CopyBytes);

internal interface IInteropImportPlanner
{
    InteropImportPlan Build(WasmEmissionRequest request, int firstIndex);
}

internal sealed class InteropImportPlanner : IInteropImportPlanner
{
    public InteropImportPlan Build(WasmEmissionRequest request, int firstIndex)
    {
        var hasStrings = request.JSImportMethods.Any(method =>
            InteropTypeClassifier.IsString(method.Signature.ReturnSignatureType)) ||
            request.HostCallbacks.Any(callback => callback.Invoke.Signature
                .ParameterSignatureTypes.Any(InteropTypeClassifier.IsString));
        var hasBytes = request.JSImportMethods.Any(method =>
            InteropTypeClassifier.IsByteArray(method.Signature.ReturnSignatureType)) ||
            request.HostCallbacks.Any(callback => callback.Invoke.Signature
                .ParameterSignatureTypes.Any(InteropTypeClassifier.IsByteArray));
        var hasObjects = request.JSImportMethods.Any(method =>
            InteropTypeClassifier.IsHostObject(method.Signature.ReturnSignatureType) ||
            method.Signature.ParameterSignatureTypes.Any(InteropTypeClassifier.IsHostObject));
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
            copyBytes);
    }

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
