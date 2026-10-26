using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IJSImportForwarderPlanner
{
    WasmModulePlan Plan(WasmEmissionRequest request, WasmModulePlan plan);
}

// A delegate invokes its target with the managed signature, but a JSImport's Wasm
// import has the interop ABI. Each JSImport that a delegate binds directly gets one
// managed-ABI forwarder function, placed before the static initializer functions.
internal sealed class JSImportForwarderPlanner(ISymbolFormatter symbols) : IJSImportForwarderPlanner
{
    public WasmModulePlan Plan(WasmEmissionRequest request, WasmModulePlan plan)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);

        var jsImports = request.JSImportMethods.Select(method => method.Key).ToHashSet();
        var targets = request.DelegateBindings
            .Select(binding => binding.Target)
            .Where(target => jsImports.Contains(target.Definition.Key))
            .DistinctBy(target => target.Definition.Key)
            .OrderBy(target => target.Definition.Key.Assembly.Name, StringComparer.Ordinal)
            .ThenBy(target => target.Definition.Key.MetadataToken)
            .ToImmutableArray();
        foreach (var target in targets)
        {
            var key = target.Definition.Key;
            if (request.HostCallbacks.Any(callback => callback.ImportMethod == key))
            {
                throw Unsupported(target, "registers a JavaScript callback");
            }
            if (request.JavaScriptAsyncBindings.ContainsKey(key))
            {
                throw Unsupported(target, "returns a Task or ValueTask");
            }
            var result = target.Signature.ReturnSignatureType;
            if (JavaScriptConversion.IsDate(result) || JavaScriptNullable.IsSupported(result))
            {
                throw Unsupported(target, "returns a DateTime, DateTimeOffset or nullable value");
            }
        }
        if (targets.IsEmpty)
        {
            return plan;
        }

        var first = plan.StaticInitializerFunctionBase;
        return plan with
        {
            FunctionIndices = plan.FunctionIndices with
            {
                JSImportForwarders = targets
                    .Select((target, index) => (target.Definition.Key, Index: new WasmFunctionIndex(first + index)))
                    .ToImmutableDictionary(item => item.Key, item => item.Index),
            },
            JSImportForwarders = targets,
            StaticInitializerFunctionBase = checked(first + targets.Length),
        };
    }

    private CompilerException Unsupported(MethodInstanceModel target, string shape) => new(
        new CompilerDiagnostic(
            DiagnosticCode.UnsupportedMetadata,
            $"A delegate cannot bind directly to JSImport method '{symbols.Format(target.Definition)}' " +
            $"because it {shape}. Bind a lambda that calls the method instead.",
            target.Definition.Name));
}
