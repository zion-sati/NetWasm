// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IJSImportForwarderFunctionAppender
{
    void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        ImmutableArray<MethodInstanceModel> forwarders,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices);
}

internal sealed class JSImportForwarderFunctionAppender(
    IJSImportForwarderFunctionEmitter emitter,
    IManagedMethodFunctionTypeResolver functionTypes) : IJSImportForwarderFunctionAppender
{
    public void Append(
        IList<WasmFunctionDefinition> functions,
        int importCount,
        ImmutableArray<MethodInstanceModel> forwarders,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(functionIndices);

        foreach (var import in forwarders)
        {
            if (checked(importCount + functions.Count) != functionIndices.Resolve(import.Definition.Key))
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.CompilerInvariant,
                    "JSIMPORT_FORWARDER_INDEX: a JSImport forwarder is not at its planned function index.",
                    import.Definition.Name));
            }

            functions.Add(new(
                $"jsimport.forwarder<{import.CanonicalName}>",
                functionTypes.Resolve(import),
                emitter.Emit(import, target, functionIndices)));
        }
    }
}
