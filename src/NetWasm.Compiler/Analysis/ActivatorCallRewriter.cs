using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ActivatorCallRewriter(
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    public CilMethodBody Rewrite(CilMethodBody body)
    {
        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 0; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.Operation != CilOperation.Call ||
                call.Operand is not CilOperand.MethodInstance activator ||
                symbols.Format(activator.Value.Definition.DeclaringType) !=
                    "System.Activator" ||
                activator.Value.Definition.Name != "CreateInstance")
            {
                continue;
            }
            var createdType = AssertSingleClosedArgument(activator.Value);
            var definition = typeDefinitions.ResolveTypeIdentity(createdType);
            var constructor = definition.Methods
                .Select(methods.GetMethod)
                .SingleOrDefault(candidate =>
                    candidate.Name == ".ctor" &&
                    !candidate.IsStatic &&
                    candidate.Signature.ParameterTypes.IsEmpty);
            if (constructor is null)
            {
                if (!createdType.IsValueType)
                {
                    throw new CompilerException(new CompilerDiagnostic(
                        DiagnosticCode.UnsupportedMetadata,
                        $"closed new() type '{createdType.CanonicalName}' has no " +
                        "parameterless constructor"));
                }
                instructions[index] = call with
                {
                    Operation = CilOperation.DefaultValue,
                    Operand = new CilOperand.TypeIdentity(createdType),
                };
            }
            else
            {
                instructions[index] = call with
                {
                    Operation = CilOperation.NewObject,
                    Operand = new CilOperand.MethodInstance(ConstructedInstance(
                        constructor,
                        createdType.Shape == CliTypeShape.GenericInstantiation
                            ? createdType.TypeArguments
                            : [])),
                };
            }
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private static CliTypeIdentity AssertSingleClosedArgument(MethodInstanceModel method)
    {
        if (method.MethodArguments.Length != 1 ||
            method.MethodArguments[0].Shape is CliTypeShape.GenericMethodParameter or
                CliTypeShape.GenericTypeParameter)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                "Activator.CreateInstance<T>() requires one closed type argument"));
        }
        return method.MethodArguments[0];
    }

    private MethodInstanceModel ConstructedInstance(
        MethodDefinitionModel method,
        ImmutableArray<CliTypeIdentity> typeArguments) =>
        methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            symbols.Format(method),
            0,
            new CliGenericContext(typeArguments, []));
}
