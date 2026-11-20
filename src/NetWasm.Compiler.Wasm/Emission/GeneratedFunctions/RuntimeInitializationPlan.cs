// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record ModuleInitializerCall(int GuardAddress, int FunctionIndex);

internal sealed record RuntimeInitializationPlan
{
    public RuntimeInitializationPlan(
        int staticDataEnd,
        StackTraceMethodPlan stackTraceMethods,
        RuntimeImportSelection runtimeImportSelection = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(staticDataEnd);
        ArgumentNullException.ThrowIfNull(stackTraceMethods);
        StaticDataEnd = staticDataEnd;
        StackTraceMethods = stackTraceMethods;
        RuntimeImportSelection = runtimeImportSelection;
    }

    public int StaticDataEnd { get; }

    public StackTraceMethodPlan StackTraceMethods { get; }

    public RuntimeImportSelection RuntimeImportSelection { get; }

    public ImmutableArray<ModuleInitializerCall> ModuleInitializers { get; init; } = [];

    public int? NativeCallbackReadinessAddress { get; init; }

    // Present when exports or host callbacks can re-enter the initializer; type, value-type
    // and static-root registration then runs on the first entry only.
    public int? StateRegistrationGuardAddress { get; init; }

    // The sidecar descriptor's address, which the initializer stores in the CoreLib word at
    // SidecarSlotAddress for CoreLib's sidecar readers.
    public int? SidecarDescriptorAddress { get; init; }
    public int? SidecarSlotAddress { get; init; }

    // The generated function every entry calls: it readies the runtime state, then runs the
    // module initializers, each once.
    public int? InitializerFunctionIndex { get; init; }

    // The generated function that readies the runtime state and runs no managed code. A
    // component's initialize export calls it during instantiation, which may not wait, while a
    // module initializer may wait as any entry's code may. Without module initializers it is
    // the function every entry calls.
    public int? StateInitializerFunctionIndex { get; init; }
}
