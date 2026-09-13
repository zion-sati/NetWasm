using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow;

public sealed record ManagedMethodBody
{
    public ManagedMethodBody(
        MethodInstanceModel method,
        ValidatedControlFlowGraph controlFlow)
    {
        Method = method ?? throw new ArgumentNullException(nameof(method));
        ControlFlow = controlFlow ?? throw new ArgumentNullException(nameof(controlFlow));
        var body = controlFlow.Graph.MethodBody;
        if (body.Method.Key != method.Definition.Key ||
            body.MethodInstance?.CanonicalName != method.CanonicalName)
        {
            throw new ArgumentException(
                "Validated control flow does not belong to the supplied method instance.",
                nameof(controlFlow));
        }
    }

    public MethodInstanceModel Method { get; }

    public ValidatedControlFlowGraph ControlFlow { get; }

    public CilMethodBody Body => ControlFlow.Graph.MethodBody;
}
