using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed record ModuleImportCollectionRequest(
    WasmEmissionRequest Emission,
    IReadOnlyList<WasmFunctionImport> RuntimeImports,
    IReadOnlyList<WasmFunctionImport> InteropImports,
    WasmTarget Target,
    ImmutableDictionary<(EntityKey Method, int ParameterIndex), HostCallbackDeclaration>
        HostCallbacks);
