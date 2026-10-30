using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ConstrainedCallRewriter(
    ITypeFinder types,
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
            var enclosingTypeArguments = body.MethodInstance?.DeclaringType.Shape ==
                CliTypeShape.GenericInstantiation
                    ? body.MethodInstance.DeclaringType.TypeArguments
                    : ImmutableArray<CliTypeIdentity>.Empty;
            var enclosingMethodArguments = body.MethodInstance?.MethodArguments ??
                ImmutableArray<CliTypeIdentity>.Empty;
            var constrainedType = constrained.Value.Substitute(
                enclosingTypeArguments,
                enclosingMethodArguments);
            if (constrainedType.Shape is not (CliTypeShape.Primitive or
                    CliTypeShape.Named or CliTypeShape.GenericInstantiation))
            {
                continue;
            }
            var concreteType = typeDefinitions.ResolveTypeIdentity(constrainedType);
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
            var typeArguments = constrainedType.Shape ==
                CliTypeShape.GenericInstantiation
                    ? constrainedType.TypeArguments
                    : [];
            var methodArguments = call.Operand is CilOperand.MethodInstance methodCall
                ? methodCall.Value.MethodArguments
                : [];
            var callSignature = call.Operand is CilOperand.MethodInstance instanceCall
                ? instanceCall.Value.Signature
                : declaration.Signature;
            var implementation = targets.Resolve(new ConstrainedCallTargetRequest(
                constrainedType,
                concreteType,
                declaration,
                callSignature,
                typeArguments,
                methodArguments));
            if (call.Operation is CilOperation.Call or CilOperation.CallVirtual &&
                InheritedValueTypeToString(concreteType, implementation, constrainedType) is { } byReference)
            {
                instructions[index - 1] = instructions[index - 1] with
                {
                    Operation = CilOperation.Nop,
                    Operand = new CilOperand.None(),
                };
                instructions[index] = call with
                {
                    Operation = CilOperation.Call,
                    Operand = new CilOperand.MethodInstance(byReference),
                };
                changed = true;
                continue;
            }
            var directOperation = call.Operation switch
            {
                // Resolving a sealed reference target does not prove its receiver
                // non-null. Keep callvirt so emission checks the loaded reference,
                // even when the target body never dereferences this.
                CilOperation.CallVirtual when !concreteType.IsValueType => CilOperation.CallVirtual,
                CilOperation.Call or CilOperation.CallVirtual => CilOperation.Call,
                _ => CilOperation.LoadFunction,
            };
            // A value type's own instance method takes the managed pointer the
            // prefix receives, as a call through the address does, so only an
            // inherited or default implementation still needs the prefix. An
            // enum declares no methods, so its calls keep it.
            if (call.Operation == CilOperation.Call ||
                directOperation == CilOperation.Call && concreteType.IsValueType &&
                !implementation.IsStatic && implementation.DeclaringType == concreteType.Key)
            {
                instructions[index - 1] = instructions[index - 1] with
                {
                    Operation = CilOperation.Nop,
                    Operand = new CilOperand.None(),
                };
            }
            var implementationTypeArguments = implementation.Key == declaration.Key &&
                call.Operand is CilOperand.MethodInstance declarationCall &&
                declarationCall.Value.DeclaringType.Shape == CliTypeShape.GenericInstantiation
                    ? declarationCall.Value.DeclaringType.TypeArguments
                    : typeArguments;
            var resolved = ConstructedInstance(
                implementation,
                implementationTypeArguments,
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

    // A value type that inherits a parameterless ToString from Object or ValueType still has its
    // managed pointer on the stack, not a box, so a direct call would read the value as an object.
    // CoreLib's by-reference helper answers with the value type's name, as the boxed call does.
    private MethodInstanceModel? InheritedValueTypeToString(
        TypeDefinitionModel concreteType,
        MethodDefinitionModel implementation,
        CliTypeIdentity constrainedType)
    {
        if (!concreteType.IsValueType || concreteType.IsEnum || implementation.IsStatic ||
            implementation.Name != "ToString" || !implementation.Signature.ParameterTypes.IsEmpty ||
            symbols.Format(implementation.DeclaringType) is not ("System.Object" or "System.ValueType"))
        {
            return null;
        }
        var helper = types.FindType("System.Runtime.CompilerServices.RuntimeHelpers").Methods
            .Select(methods.GetMethod)
            .Single(method => method.Name == "ValueTypeToString" && method.GenericArity == 1);
        return ConstructedInstance(helper, [], [constrainedType]);
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
