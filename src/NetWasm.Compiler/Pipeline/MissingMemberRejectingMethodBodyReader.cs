// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Pipeline;

// For a project that makes NW1016 an error (NetWasmMissingApiAsError): the analysis reads a method naming a member
// its declaring type lacks as the failure it is, where it reaches the method, so the error names the package and the
// path that reached it.
internal sealed class MissingMemberRejectingMethodBodyReader(IMethodBodyReader bodies) : IMethodBodyReader
{
    private readonly IMethodBodyReader _bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));

    public CilMethodBody ReadMethodBody(MethodDefinitionModel method) => Reject(_bodies.ReadMethodBody(method));

    public CilMethodBody ReadMethodBody(MethodInstanceModel method) => Reject(_bodies.ReadMethodBody(method));

    private static CilMethodBody Reject(CilMethodBody body) => body.MissingMember is { } missing
        ? throw new CompilerException(new(DiagnosticCode.MissingApi, missing.Message, missing.Method, missing.IlOffset)
        {
            MissingMember = missing,
        })
        : body;
}
