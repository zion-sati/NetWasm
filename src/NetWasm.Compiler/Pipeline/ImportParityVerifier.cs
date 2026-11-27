// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

internal interface IImportParityVerifier
{
    void Verify(ReachableProgram program, ReachableProgram reference, ISymbolFormatter symbols);
}

// An optimization level never changes which host imports a program has (WIT, JavaScript and native import methods):
// a host that withholds a capability would reject one level and run another. Fails naming each import only one of
// the two programs reaches, with how that program reached it.
internal sealed class ImportParityVerifier : IImportParityVerifier
{
    public void Verify(ReachableProgram program, ReachableProgram reference, ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(symbols);
        var imports = Imports(program);
        var referenceImports = Imports(reference);
        var extra = imports.Keys.Except(referenceImports.Keys)
            .Select(key => Describe(program, imports[key], symbols, "only this level reaches"));
        var missing = referenceImports.Keys.Except(imports.Keys)
            .Select(key => Describe(reference, referenceImports[key], symbols, "only the reference level reaches"));
        var mismatches = extra.Concat(missing).Order(StringComparer.Ordinal).ToArray();
        if (mismatches.Length != 0)
        {
            throw new InvalidOperationException("host imports differ between optimization levels:\n" +
                string.Join("\n", mismatches));
        }
    }

    private static Dictionary<EntityKey, MethodDefinitionModel> Imports(ReachableProgram program)
    {
        var imports = new Dictionary<EntityKey, MethodDefinitionModel>();
        foreach (var method in program.WitImportMethods.Concat(program.JSImportMethods).Concat(program.MethodInstances.Values
            .Select(instance => instance.Definition)
            .Where(definition => definition.NativeImport is not null)))
        {
            imports.TryAdd(method.Key, method);
        }
        return imports;
    }

    private static string Describe(ReachableProgram program, MethodDefinitionModel method, ISymbolFormatter symbols, string side)
    {
        var path = program.ImportDependencyPaths.TryGetValue(method.Key, out var steps)
            ? string.Join(" <- ", steps.Select(step => step.Source.InstanceIdentity ?? step.Source.Source.ToString()))
            : "(no path recorded)";
        return $"  {symbols.Format(method)}: {side}, reached through {path}";
    }
}
