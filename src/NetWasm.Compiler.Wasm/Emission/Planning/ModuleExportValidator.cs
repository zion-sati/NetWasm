using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class ModuleExportValidator : IModuleExportValidator
{
    public void Validate(
        IReadOnlyCollection<WasmExport> exports,
        IReadOnlyDictionary<string, int> nativeCallbacks)
    {
        ArgumentNullException.ThrowIfNull(exports);
        ArgumentNullException.ThrowIfNull(nativeCallbacks);

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in exports)
        {
            if (names.Add(export.Name))
            {
                continue;
            }

            var nativeCollision = nativeCallbacks.ContainsKey(export.Name);
            throw new CompilerException(new(
                nativeCollision
                    ? DiagnosticCode.NativeInterop
                    : DiagnosticCode.CompilerInvariant,
                nativeCollision
                    ? "A named native callback collides with another application export."
                    : "Application export names must be unique."));
        }
    }
}
