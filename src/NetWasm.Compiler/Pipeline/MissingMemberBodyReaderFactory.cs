// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

internal interface IMissingMemberBodyReaderFactory
{
    // The reader the analysis takes: the given one, or, when the project makes NW1016 an error
    // (NetWasmMissingApiAsError), one that fails where the analysis reaches a method naming a missing member.
    IMethodBodyReader Create(IMethodBodyReader bodies, bool missingApiAsError);
}

internal sealed class MissingMemberBodyReaderFactory : IMissingMemberBodyReaderFactory
{
    public IMethodBodyReader Create(IMethodBodyReader bodies, bool missingApiAsError)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        return missingApiAsError ? new MissingMemberRejectingMethodBodyReader(bodies) : bodies;
    }
}
