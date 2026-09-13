using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ConstrainedCallRewriter(
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols,
    IConstrainedCallTargetResolver targets) : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 1; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.Operation is not (CilOperation.Call or
                    CilOperation.CallVirtual or CilOperation.LoadVirtualFunction) ||
                instructions[index - 1].Operation != CilOperation.Constrained ||
                instructions[index - 1].Operand is not CilOperand.TypeIdentity constrained)
            {
                continue;
            }
            if (constrained.Value.Shape is not (CliTypeShape.Primitive or
                    CliTypeShape.Named or CliTypeShape.GenericInstantiation))
            {
                continue;
            }
            var concreteType = typeDefinitions.ResolveTypeIdentity(constrained.Value);
            if (!concreteType.IsValueType && !concreteType.IsSealed)
            {
                continue;
            }
            var declaration = call.Operand switch
            {
                CilOperand.Entity entity => methods.GetMethod(entity.Key),
                CilOperand.MethodInstance instance => instance.Value.Definition,
                _ => throw new InvalidOperationException(
                    "constrained call has no method operand"),
            };
            var typeArguments = constrained.Value.Shape ==
                CliTypeShape.GenericInstantiation
                    ? constrained.Value.TypeArguments
                    : [];
            var methodArguments = call.Operand is CilOperand.MethodInstance methodCall
                ? methodCall.Value.MethodArguments
                : [];
            var callSignature = call.Operand is CilOperand.MethodInstance instanceCall
                ? instanceCall.Value.Signature
                : declaration.Signature;
            var implementation = targets.Resolve(new ConstrainedCallTargetRequest(
                constrained.Value,
                concreteType,
                declaration,
                callSignature,
                typeArguments,
                methodArguments));
            var directOperation = call.Operation switch
            {
                CilOperation.Call or CilOperation.CallVirtual => CilOperation.Call,
                _ => CilOperation.LoadFunction,
            };
            if (call.Operation == CilOperation.Call)
            {
                instructions[index - 1] = instructions[index - 1] with
                {
                    Operation = CilOperation.Nop,
                    Operand = new CilOperand.None(),
                };
            }
            var resolved = ConstructedInstance(
                implementation,
                typeArguments,
                methodArguments);
            instructions[index] = call with
            {
                Operation = directOperation,
                Operand = new CilOperand.MethodInstance(resolved),
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private MethodInstanceModel ConstructedInstance(
        MethodDefinitionModel method,
        ImmutableArray<CliTypeIdentity> typeArguments,
        ImmutableArray<CliTypeIdentity> methodArguments) =>
        methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            symbols.Format(method),
            0,
            new CliGenericContext(typeArguments, methodArguments));

}
