using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Gives a method whose value the project's feature switches select the body that returns it, as ILLink stubs it: a
// Boolean method returns the value, and a stubbed void method returns at once (EventSource.Finalize() while
// EventSource is unsupported). A call FeatureSwitchCallRewriter cannot fold then runs the stub, and the code the
// original body reached is not reached through it.
internal sealed class FeatureSwitchBodyStubRewriter(IFeatureSwitchValueSelector values) : IMethodRewriteRule
{
    private readonly IFeatureSwitchValueSelector _values = values ?? throw new ArgumentNullException(nameof(values));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (_values.Select(body.Method) is not { } value)
        {
            return body;
        }

        ImmutableArray<CilInstruction> instructions = body.Method.Signature.ReturnType == CliValueKind.Void
            ? [new(0, 1, CilOperation.Return, new CilOperand.None())]
            : [new(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(value ? 1 : 0)), new(1, 2, CilOperation.Return, new CilOperand.None())];
        return new CilMethodBody(body.Method, 1, [], instructions) { MethodInstance = body.MethodInstance };
    }
}
