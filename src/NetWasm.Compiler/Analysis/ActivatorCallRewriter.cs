using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// Activator.CreateInstance<T>() is managed code, as upstream's: it asks HasDefaultConstructor<T>()
// and HasNonPublicDefaultConstructor<T>() about the closed T, and constructs through
// CreateDefaultInstance<T>() inside the handler that wraps a constructor's exception in
// TargetInvocationException. This rule answers both questions and replaces the construction as
// .NET classifies T: a value type is constructed, or takes its zero value when it declares no
// parameterless constructor; a concrete class with a public parameterless constructor is
// constructed; a non-public parameterless constructor is reported as such; string, arrays,
// interfaces, abstract classes and classes without a parameterless constructor leave the managed
// body to throw what .NET throws for them. A delegate to CreateInstance<T> runs the same body.
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
                activator.Value.Definition.Name is not
                    ("HasDefaultConstructor" or "HasNonPublicDefaultConstructor" or "CreateDefaultInstance"))
            {
                continue;
            }
            var createdType = AssertSingleClosedArgument(activator.Value);
            var (kind, constructor) = Classify(createdType);
            switch (activator.Value.Definition.Name, kind)
            {
                case ("HasDefaultConstructor", _):
                    instructions[index] = Constant(call, kind is Construction.ZeroValue or Construction.Constructor);
                    break;
                case ("HasNonPublicDefaultConstructor", _):
                    instructions[index] = Constant(call, kind is Construction.NonPublicConstructor);
                    break;
                case (_, Construction.ZeroValue):
                    instructions[index] = call with
                    {
                        Operation = CilOperation.DefaultValue,
                        Operand = new CilOperand.TypeIdentity(createdType),
                    };
                    break;
                case (_, Construction.Constructor):
                    instructions[index] = call with
                    {
                        Operation = CilOperation.NewObject,
                        Operand = new CilOperand.MethodInstance(ConstructedInstance(
                            constructor!,
                            createdType.Shape == CliTypeShape.GenericInstantiation
                                ? createdType.TypeArguments
                                : [])),
                    };
                    break;
                default:
                    // The managed wrapper throws for this T before it constructs.
                    continue;
            }
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private (Construction Kind, MethodDefinitionModel? Constructor) Classify(CliTypeIdentity createdType)
    {
        if (createdType.Shape is not (CliTypeShape.Primitive or CliTypeShape.Named or
                CliTypeShape.GenericInstantiation) ||
            createdType.CanonicalName == "primitive:string")
        {
            return (Construction.None, null);
        }
        var definition = typeDefinitions.ResolveTypeIdentity(createdType);
        if (!createdType.IsValueType && (definition.IsInterface || definition.IsAbstract))
        {
            return (Construction.None, null);
        }
        var constructor = definition.Methods
            .Select(methods.GetMethod)
            .SingleOrDefault(candidate =>
                candidate.Name == ".ctor" &&
                !candidate.IsStatic &&
                candidate.Signature.ParameterTypes.IsEmpty);
        return constructor switch
        {
            null when createdType.IsValueType => (Construction.ZeroValue, null),
            null => (Construction.None, null),
            { IsPublic: true } => (Construction.Constructor, constructor),
            _ => (Construction.NonPublicConstructor, null),
        };
    }

    private static CilInstruction Constant(CilInstruction call, bool value) => call with
    {
        Operation = CilOperation.LoadInt32,
        Operand = new CilOperand.ConstantI4(value ? 1 : 0),
    };

    private static CliTypeIdentity AssertSingleClosedArgument(MethodInstanceModel method)
    {
        if (method.MethodArguments.Length != 1 ||
            method.MethodArguments[0].ContainsGenericParameters)
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

    private enum Construction
    {
        None,
        ZeroValue,
        Constructor,
        NonPublicConstructor,
    }
}
