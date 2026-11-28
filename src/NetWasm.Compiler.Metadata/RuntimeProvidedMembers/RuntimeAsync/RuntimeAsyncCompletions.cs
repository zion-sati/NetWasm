// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync.RuntimeAsyncAwaits;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

// The RuntimeAsync helper of a completion (Complete, Suspended, Faulted and their Virtual forms) for a method's result
// shape: the Task helper, its generic form over the result, or the ValueTask ones (the name with Value).
internal static class RuntimeAsyncCompletions
{
    internal static MethodInstanceModel Resolve(IRuntimeAsyncMemberResolver runtime, string name, ResultShape shape,
        CliTypeIdentity? result) => shape switch
        {
            ResultShape.Task => runtime.Resolve(RuntimeAsyncMember.Helper(name)),
            ResultShape.TaskOfResult => runtime.Resolve(new RuntimeAsyncMember.Named(name, [result!])),
            ResultShape.ValueTask => runtime.Resolve(RuntimeAsyncMember.Helper(name + "Value")),
            _ => runtime.Resolve(new RuntimeAsyncMember.Named(name + "Value", [result!])),
        };
}
