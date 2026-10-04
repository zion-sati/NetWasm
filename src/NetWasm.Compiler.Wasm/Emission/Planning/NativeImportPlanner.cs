using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class NativeImportPlanner(INativeAbiPlanner abi) : INativeImportPlanner
{
    private readonly INativeAbiPlanner _abi = abi ?? throw new ArgumentNullException(nameof(abi));

    public NativeImportPlan Plan(IEnumerable<MethodInstanceModel> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var imports = new Dictionary<EntityKey, NativeMethodImport>();
        foreach (var method in methods)
        {
            if (method.Definition.NativeImport is null)
                continue;
            if (imports.TryGetValue(method.Definition.Key, out var existing))
            {
                if (!existing.Method.HasEquivalentDescriptorFacts(method))
                    throw new CompilerException(new(DiagnosticCode.NativeInterop,
                        "A native method has contradictory reached declarations.", method.CanonicalName));
                continue;
            }
            var plan = _abi.Plan(method);
            imports.Add(method.Definition.Key, new(method, plan,
                new(RuntimeAbi.RuntimeModule, plan.Import.EntryPoint,
                    new(plan.Signature.ParameterTypes, plan.Signature.ReturnType))));
        }
        return new([.. imports.Values.OrderBy(import => import.Method.CanonicalName, StringComparer.Ordinal)]);
    }
}
