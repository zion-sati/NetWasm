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
            body = read();
            return true;
        }
        catch (CompilerException)
        {
            body = null;
            return false;
        }
    }
}
