// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Diagnostics.CodeAnalysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class CalleeMethodBodyReader(IMethodBodyReader bodies) : ICalleeMethodBodyReader
{
    private readonly IMethodBodyReader _bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));

    public bool TryReadMethodBody(MethodDefinitionModel method, [NotNullWhen(true)] out CilMethodBody? body) =>
        TryRead(() => _bodies.ReadMethodBody(method), out body);

    public bool TryReadMethodBody(MethodInstanceModel method, [NotNullWhen(true)] out CilMethodBody? body) =>
        TryRead(() => _bodies.ReadMethodBody(method), out body);

    private static bool TryRead(Func<CilMethodBody> read, [NotNullWhen(true)] out CilMethodBody? body)
    {
        try
        {
            // A body that only throws for a member its IL names but NetWasm lacks is no body to analyze or inline,
            // as .NET does not inline a method it cannot compile.
            var decoded = read();
            body = decoded.MissingMember is null ? decoded : null;
            return body is not null;
        }
        catch (CompilerException)
        {
            body = null;
            return false;
        }
    }
}
