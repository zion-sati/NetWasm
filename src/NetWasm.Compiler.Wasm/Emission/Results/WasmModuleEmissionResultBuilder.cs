using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.NativeInterop;

namespace NetWasm.Compiler.Wasm.Emission.Results;

internal sealed class WasmModuleEmissionResultBuilder(
    IWasmModuleBuilder modules,
    IManagedMethodEmissionMetricProjector metrics,
    INativeCallbackObjectWriter callbackObjects) :
    IWasmModuleEmissionResultBuilder
{
    public WasmModuleEmissionResult Build(WasmModuleEmissionBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var module = request.Module;
        var target = WasmTargetLayout.For(module.Target);
        var nativeImports = request.NativeImports.Methods.Select(import => new WasmNativeImport(
            import.Abi.Import.LibraryName,
            import.Abi.Import.EntryPoint,
            [.. import.Import.Type.Parameters.Select(parameter => WasmValueTypes.FromCli(parameter, target))],
            import.Import.Type.Result == CliValueKind.Void
                ? null
                : WasmValueTypes.FromCli(import.Import.Type.Result, target))).ToImmutableArray();
        var callbackObject = callbackObjects.Write(request.NativeCallbacks, module.Target);
        var callbackSupport = callbackObject.Length == 0
            ? null
            : new WasmNativeCallbackSupportArtifact(
                callbackObject,
                Convert.ToHexString(SHA256.HashData(callbackObject)).ToLowerInvariant(),
                [.. request.NativeCallbacks.Methods.Select(callback =>
                    new WasmNativeCallbackDescriptor(
                        callback.NativeSymbol,
                        callback.RuntimeImportSymbol,
                        callback.ThunkExportName,
                        callback.GetterName,
                        [.. callback.Abi.PhysicalSignature.ParameterTypes.Select(
                            parameter => WasmValueTypes.FromCli(parameter, target))],
                        callback.Abi.PhysicalSignature.ReturnType == CliValueKind.Void
                            ? null
                            : WasmValueTypes.FromCli(
                                callback.Abi.PhysicalSignature.ReturnType,
                                target)))],
                [.. request.NativeCallbacks.Methods.Select(callback =>
                    callback)
                    .Where(callback => !callback.IsNamed)
                    .Select(callback => callback.ThunkExportName)],
                [.. request.NativeCallbacks.AddressedMethods.Select(callback =>
                    callback.GetterName!)]);
        return new(
            modules.Build(
                module.FunctionImports,
                module.MemoryImportModule,
                module.MemoryImportName,
                module.Functions,
                module.Exports,
                module.DataSegments,
                module.IncludeManagedExceptionTag,
                module.Target,
                module.IncludeNameSection),
            request.StaticDataEnd,
            metrics.Project(request.ManagedMethodEmissions),
            request.StackTraceSymbols)
        {
            FunctionImports = [.. module.FunctionImports],
            NativeImports = nativeImports,
            RuntimeFeatures = request.RuntimeFeatures,
            NativeCallbackSupport = callbackSupport,
        };
    }
}
