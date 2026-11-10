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
            if (Replacement(activator.Value, createdType) is not { } replacement)
            {
                continue;
            }
            instructions[index] = call with
            {
                Operation = replacement.Operation,
                Operand = replacement.Operand,
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    // What Activator.CreateInstance<T>() compiles to, as .NET behaves: construction for a value
    // type or a concrete class with a public parameterless constructor, a value type's zero
    // value when it declares none, and a throw of MissingMethodException for a parameterless
    // constructor that is not public. Null leaves the call, whose body throws what .NET throws
    // for string, arrays, pointers, interfaces, abstract classes and classes without a
    // parameterless constructor.
    private (CilOperation Operation, CilOperand Operand)? Replacement(
        MethodInstanceModel activator,
        CliTypeIdentity createdType)
    {
        if (createdType.Shape is not (CliTypeShape.Primitive or CliTypeShape.Named or
                CliTypeShape.GenericInstantiation) ||
            createdType.CanonicalName == "primitive:string")
        {
            return null;
        }
        var definition = typeDefinitions.ResolveTypeIdentity(createdType);
        if (!createdType.IsValueType && (definition.IsInterface || definition.IsAbstract))
        {
            return null;
        }
        var constructor = definition.Methods
            .Select(methods.GetMethod)
            .SingleOrDefault(candidate =>
                candidate.Name == ".ctor" &&
                !candidate.IsStatic &&
                candidate.Signature.ParameterTypes.IsEmpty);
        return constructor switch
        {
            null when createdType.IsValueType =>
                (CilOperation.DefaultValue, new CilOperand.TypeIdentity(createdType)),
            null => null,
            { IsPublic: true } =>
                (CilOperation.NewObject, new CilOperand.MethodInstance(ConstructedInstance(
                    constructor,
                    createdType.Shape == CliTypeShape.GenericInstantiation
                        ? createdType.TypeArguments
                        : []))),
            _ => (CilOperation.Call, new CilOperand.MethodInstance(
                NonPublicConstructorThrow(activator, createdType))),
        };
    }

    private MethodInstanceModel NonPublicConstructorThrow(
        MethodInstanceModel activator,
        CliTypeIdentity createdType)
    {
        var assembly = activator.Definition.Key.Assembly;
        var thrower = typeDefinitions
            .ResolveTypeIdentity(CliTypeIdentity.Named(assembly, "System", "Activator", false))
            .Methods
            .Select(methods.GetMethod)
            .Single(method => method.Name == "CreateInstanceWithNonPublicConstructor");
        return methodInstances.ResolveMethodInstance(
            assembly,
            thrower.Key.MetadataToken,
            symbols.Format(thrower),
            0,
            new CliGenericContext([], [createdType]));
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
