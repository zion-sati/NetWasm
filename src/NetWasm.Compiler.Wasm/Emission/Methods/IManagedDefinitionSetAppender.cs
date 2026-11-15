// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IManagedDefinitionSetAppender
{
    void Append(IList<WasmFunctionDefinition> functions,
        IDictionary<string, FilterEnvironmentLayout> filterEnvironments,
        ICollection<ManagedMethodEmissionRecord> emissions,
        ImmutableArray<ManagedDefinitionEmission> orderedMethods,
        IReadOnlyDictionary<EntityKey, StructuredMethod> methods,
        IReadOnlyDictionary<EntityKey, MethodRootMap> rootMaps,
        InstructionModuleTarget target, IFunctionIndexResolver functionIndices);
}
