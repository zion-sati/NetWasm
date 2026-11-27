// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

internal interface IRuntimeAsyncSourceReader
{
    // The CIL a runtime-async method's lowering (RuntimeAsyncBodyBuilder) takes: its machine's MoveNext rewritten for a
    // method Roslyn compiled to a state machine (StateMachineBodyBuilder), an async iterator member's body
    // (AsyncIteratorBodyBuilder), or the method's own body.
    CilMethodBody Read(MethodInstanceModel method);
}

internal sealed class RuntimeAsyncSourceReader(
    ICilDecoderFactory decoders,
    IManagedAssemblyResolver assemblies,
    IStateMachineBodyBuilder stateMachines,
    IStateMachineBodyBuilder asyncIterators) : IRuntimeAsyncSourceReader
{
    public CilMethodBody Read(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.Definition switch
        {
            { StateMachineMoveNext: not null } => stateMachines.Build(method),
            { AsyncIteratorMoveNext: not null } or { AsyncIteratorDisposeOf: not null } => asyncIterators.Build(method),
            _ => decoders.Create().Decode(assemblies.Resolve(method.Definition.Key.Assembly), method),
        };
    }
}
