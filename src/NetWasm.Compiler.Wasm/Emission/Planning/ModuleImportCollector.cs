using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class ModuleImportCollector(
    ISymbolFormatter symbols,
    ITargetLayout layouts,
    IJavaScriptImportParameterTypeResolver javaScriptParameterTypes,
    ICanonicalAbiFunctionTypePlanner canonicalTypes) : IModuleImportCollector
{
    public ImmutableArray<WasmFunctionImport> Collect(
        ModuleImportCollectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var emission = request.Emission;
        var imports = request.RuntimeImports.Concat(request.InteropImports)
            .Concat(emission.JSImportMethods.Select(method => new WasmFunctionImport(
                method.JSImport!.ModuleName ?? RuntimeAbi.HostModule,
                method.JSImport.FunctionName,
                WasmFunctionType.Create(
                    CliValueKind.I4,
                    [.. method.Signature.ParameterSignatureTypes.Select((type, index) =>
                        javaScriptParameterTypes.Resolve(
                            type,
                            request.HostCallbacks.ContainsKey((method.Key, index)))),
                        .. emission.JavaScriptAsyncBindings.ContainsKey(method.Key)
                            ? [CliValueKind.I4]
                            : Array.Empty<CliValueKind>(),
                        CliValueKind.ManagedAddress])))).ToArray();
        var canonicalImports = emission.ComponentContract.Imports
            .ToImmutableDictionary(function => function.Identity);
        return [.. imports.Concat(emission.WitImportMethods
            .DistinctBy(method => method.WitImport!.Identity)
            .Select(method =>
        {
            if (!canonicalImports.TryGetValue(method.WitImport!.Identity, out var function))
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.ComponentContract,
                    "reachable WIT import is absent from selected component contract",
                    symbols.Format(method)));
            }
            return new WasmFunctionImport(
                CanonicalAbiNames.ImportModule(function, layouts.Target.Target),
                CanonicalAbiNames.ImportName(function),
                canonicalTypes.Plan(
                    function,
                    CanonicalAbiDirection.LoweredImport).CoreType);
        }))];
    }
}
