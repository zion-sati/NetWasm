// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IFilterFuncletEmitter
{
    FilterFuncletEmission Emit(
        FilterFunclet filter,
        FilterEnvironmentLayout environment,
        MethodRootMap rootMap,
        IFilterSequenceEmitter emitSequence,
        MemberExecutionPlan? memberExecution = null,
        OptionalFunctionIndex managedThrowFunction = default,
        NativeImportPlan? nativeImports = null);
}
