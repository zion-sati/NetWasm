// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IFallbackPublicationClassifier
{
    // The safepoints, by instruction offset, that publish their roots only where an allocation falls back to the
    // collector, which enters a lazy root frame itself; the path through the inline allocation publishes nothing.
    ImmutableHashSet<int> Classify(StructuredMethod method, MethodEmissionContext context);
}

// A newobj that bumps its run inline publishes only on the bump's fallback (ObjectConstructionEmitter defers its roots)
// where ConditionalAllocationRoots holds, outside protected blocks and handlers, and where its constructor call is no
// safepoint of its own. A newobj the emitter cannot defer after all publishes where it stands, which enters the frame
// as any lazy publication does.
internal sealed class FallbackPublicationClassifier : IFallbackPublicationClassifier
{
    public ImmutableHashSet<int> Classify(StructuredMethod method, MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(context);
        if (!context.ConditionalAllocationRoots || !method.ExceptionGroups.IsEmpty) return [];
        return
        [
            .. method.Blocks.Values
                .SelectMany(static block => block.Instructions)
                .Where(instruction => instruction.Operation == CilOperation.NewObject &&
                    context.RootMap.Safepoints.TryGetValue(instruction.Offset, out var point) &&
                    point.ConstructorCallRoots.IsEmpty)
                .Select(static instruction => instruction.Offset),
        ];
    }
}
