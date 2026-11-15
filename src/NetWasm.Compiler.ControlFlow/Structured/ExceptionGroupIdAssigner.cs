// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using Draft = NetWasm.Compiler.ControlFlow.Draft;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class ExceptionGroupIdAssigner : IExceptionGroupIdAssigner
{
    public ImmutableDictionary<Draft.StructuredExceptionGroupDraft, StructuredExceptionGroupId> Assign(
        ImmutableArray<Draft.StructuredExceptionGroupDraft> groups)
    {
        if (groups.IsDefault)
        {
            throw new ArgumentException("Exception groups must be initialized.", nameof(groups));
        }

        var assignments = ImmutableDictionary.CreateBuilder<
            Draft.StructuredExceptionGroupDraft,
            StructuredExceptionGroupId>(
            ReferenceEqualityComparer.Instance);
        for (var index = 0; index < groups.Length; index++)
        {
            assignments.Add(groups[index], new StructuredExceptionGroupId(index));
        }

        return assignments.ToImmutable();
    }
}
