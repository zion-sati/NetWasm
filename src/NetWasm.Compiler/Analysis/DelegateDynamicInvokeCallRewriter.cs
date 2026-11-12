using System;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// Delegate.DynamicInvoke on a receiver whose static type is a concrete delegate type calls
// DelegateMetadata.DynamicInvoke<TDelegate>, so that type alone carries Invoke metadata and an
// invocation stub. Delegate types are sealed, so the static type is the receiver's type. A receiver
// typed Delegate or MulticastDelegate keeps the call, whose type is known only at run time.
internal sealed class DelegateDynamicInvokeCallRewriter(
    ICalledMethodResolver calledMethods,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols,
    IControlFlowGraphBuilder graphs,
    ITypedStackValidator stacks,
    IStaticReferenceTypeAnalyzer staticTypes) : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.Instructions.Any(instruction => DynamicInvoke(instruction) is not null))
        {
            return body;
        }
        var types = staticTypes.Analyze(stacks.Validate(graphs.Build(body)));
        var instructions = body.Instructions.ToBuilder();
        var changed = false;
        for (var index = 0; index < instructions.Count; index++)
        {
            if (DynamicInvoke(body.Instructions[index]) is not { } target ||
                !types.TryGetValue(body.Instructions[index].Offset, out var stack) ||
                stack.Length < 2 ||
                stack[^2] is not { } delegateType ||
                !IsConcreteDelegate(delegateType))
            {
                continue;
            }
            instructions[index] = body.Instructions[index] with
            {
                Operation = CilOperation.Call,
                Operand = new CilOperand.MethodInstance(Invoker(target, delegateType)),
            };
            changed = true;
        }
        return changed ? body with { Instructions = instructions.ToImmutable() } : body;
    }

    private MethodInstanceModel? DynamicInvoke(CilInstruction instruction) =>
        instruction.Operation is CilOperation.Call or CilOperation.CallVirtual &&
        calledMethods.Resolve(instruction) is { Definition: { Name: "DynamicInvoke", IsStatic: false } } target &&
        target.Signature.ParameterTypes.Length == 1 &&
        symbols.Format(target.Definition.DeclaringType) == "System.Delegate"
            ? target
            : null;

    // Delegate and MulticastDelegate are abstract, so a sealed receiver type is a delegate type.
    private bool IsConcreteDelegate(CliTypeIdentity type) =>
        type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation &&
        typeDefinitions.ResolveTypeIdentity(type).IsSealed;

    private MethodInstanceModel Invoker(MethodInstanceModel dynamicInvoke, CliTypeIdentity delegateType)
    {
        var assembly = dynamicInvoke.Definition.Key.Assembly;
        var invoker = typeDefinitions
            .ResolveTypeIdentity(CliTypeIdentity.Named(assembly, "System.Runtime.CompilerServices", "DelegateMetadata", false))
            .Methods
            .Select(methods.GetMethod)
            .Single(method => method.Name == "DynamicInvoke");
        return methodInstances.ResolveMethodInstance(
            assembly,
            invoker.Key.MetadataToken,
            symbols.Format(invoker),
            0,
            new CliGenericContext([], [delegateType]));
    }
}
