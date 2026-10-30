using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class DefaultComparerCallRewriter(
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ITypeFinder types,
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols,
    ITypeRelationshipClassifier relationships,
    IConstrainedCallTargetResolver targets,
    IStackValueConsumerFinder consumers,
    bool devirtualize) : IMethodRewriteRule
{
    private readonly NullableTypeResolver _nullableTypes = new();
    private readonly IStackValueConsumerFinder _consumers = consumers ?? throw new ArgumentNullException(nameof(consumers));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 0; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.Operation != CilOperation.Call)
            {
                continue;
            }

            var target = calledMethods.Resolve(call);
            if (target is null || target.Definition.Name != "CreateDefaultComparer" ||
                target.DeclaringType.Shape != CliTypeShape.GenericInstantiation ||
                target.DeclaringType.TypeArguments is not [var comparedType])
            {
                continue;
            }

            if (Comparer(symbols.Format(target.Definition.DeclaringType), comparedType) is not
                var (comparerDefinition, comparerType, comparerArguments))
            {
                continue;
            }

            var constructor = comparerDefinition.Methods
                .Select(methods.GetMethod)
                .Single(method => method.Name == ".ctor" &&
                    !method.IsStatic &&
                    method.Signature.ParameterTypes.IsEmpty);
            var comparerConstructor = methodInstances.ResolveMethodInstance(
                constructor.Key.Assembly,
                constructor.Key.MetadataToken,
                symbols.Format(constructor),
                0,
                new CliGenericContext(comparerArguments, []));
            instructions[index] = call with
            {
                Operation = CilOperation.NewObject,
                Operand = new CilOperand.MethodInstance(comparerConstructor with
                {
                    DeclaringType = comparerType,
                }),
            };
            changed = true;
        }
        changed |= devirtualize && DevirtualizeDefaultCalls(body, instructions);
        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    // EqualityComparer<T>.Default and Comparer<T>.Default hold the comparer
    // CreateDefaultComparer selects for T and are never null, so a virtual
    // call on what the getter returns reaches that comparer's override. The
    // call becomes direct, and the inliner can take the override and the
    // comparison it makes, as .NET's JIT devirtualizes the default comparers.
    private bool DevirtualizeDefaultCalls(CilMethodBody body, CilInstruction[] instructions)
    {
        HashSet<int>? entries = null;
        var changed = false;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            if (body.Instructions[index].Operation != CilOperation.Call ||
                calledMethods.Resolve(body.Instructions[index]) is not
                {
                    Definition.Name: "get_Default",
                    DeclaringType: { Shape: CliTypeShape.GenericInstantiation, TypeArguments: [var comparedType] },
                } getter ||
                symbols.Format(getter.Definition.DeclaringType) is var owner &&
                Comparer(owner, comparedType) is not var (definition, type, arguments))
            {
                continue;
            }
            entries ??= InstructionEntries.Of(body);
            if (_consumers.Find(body.Instructions, entries, index) is not { } consumer ||
                body.Instructions[consumer].Operation != CilOperation.CallVirtual ||
                body.Instructions[consumer - 1].Operation == CilOperation.Constrained ||
                calledMethods.Resolve(body.Instructions[consumer]) is not { Definition.IsStatic: false } declared ||
                symbols.Format(declared.Definition.DeclaringType) != owner ||
                declared.Definition.Name is not ("Equals" or "GetHashCode" or "Compare") ||
                // Only the comparer's own override; an inherited one stays virtual.
                targets.Resolve(new ConstrainedCallTargetRequest(
                    type, definition, declared.Definition, declared.Signature, arguments, [])) is not
                    {
                        IsStatic: false, HasManagedBody: true,
                    } implementation ||
                implementation.DeclaringType != definition.Key)
            {
                continue;
            }
            instructions[consumer] = instructions[consumer] with
            {
                Operation = CilOperation.Call,
                Operand = new CilOperand.MethodInstance(methodInstances.ResolveMethodInstance(
                    implementation.Key.Assembly,
                    implementation.Key.MetadataToken,
                    symbols.Format(implementation),
                    0,
                    new CliGenericContext(arguments, [])) with
                {
                    DeclaringType = type,
                }),
            };
            changed = true;
        }
        return changed;
    }

    // The comparer class the default comparer of a closed type is: its
    // definition, its type and its type arguments.
    private (TypeDefinitionModel Definition, CliTypeIdentity Type, ImmutableArray<CliTypeIdentity> Arguments)? Comparer(
        string owner,
        CliTypeIdentity comparedType)
    {
        if (Select(owner, comparedType) is not { } selection)
        {
            return null;
        }
        var definition = types.FindType(selection.ComparerName);
        var type = selection.TypeArguments.IsEmpty
            ? CliTypeIdentity.FromDefinition(definition)
            : CliTypeIdentity.GenericInstantiation(CliTypeIdentity.FromDefinition(definition), selection.TypeArguments);
        return (definition, type, selection.TypeArguments);
    }

    private Selection? Select(string owner, CliTypeIdentity comparedType)
    {
        if (comparedType.ContainsGenericParameters)
        {
            return null;
        }

        var nullableType = _nullableTypes.Resolve(comparedType);
        var isEnum = IsEnum(comparedType);
        return owner switch
        {
            "System.Collections.Generic.EqualityComparer`1" when
                comparedType.CanonicalName == "primitive:string" =>
                NonGeneric("System.Collections.Generic.StringEqualityComparer"),
            "System.Collections.Generic.EqualityComparer`1" when nullableType is not null =>
                Generic("System.Collections.Generic.NullableEqualityComparer`1", nullableType),
            "System.Collections.Generic.EqualityComparer`1" when isEnum =>
                Generic("System.Collections.Generic.EnumEqualityComparer`1", comparedType),
            "System.Collections.Generic.EqualityComparer`1" when
                Implements(comparedType, "System.IEquatable`1") =>
                Generic("System.Collections.Generic.GenericEqualityComparer`1", comparedType),
            "System.Collections.Generic.EqualityComparer`1" =>
                Generic("System.Collections.Generic.ObjectEqualityComparer`1", comparedType),
            "System.Collections.Generic.Comparer`1" when nullableType is not null =>
                Generic("System.Collections.Generic.NullableComparer`1", nullableType),
            "System.Collections.Generic.Comparer`1" when isEnum =>
                Generic("System.Collections.Generic.EnumComparer`1", comparedType),
            "System.Collections.Generic.Comparer`1" when
                Implements(comparedType, "System.IComparable`1") =>
                Generic("System.Collections.Generic.GenericComparer`1", comparedType),
            "System.Collections.Generic.Comparer`1" =>
                Generic("System.Collections.Generic.ObjectComparer`1", comparedType),
            _ => null,
        };
    }

    private bool IsEnum(CliTypeIdentity type) =>
        type.IsValueType &&
        type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation &&
        typeDefinitions.ResolveTypeIdentity(type).IsEnum;

    private bool Implements(CliTypeIdentity comparedType, string interfaceName)
    {
        var interfaceDefinition = types.FindType(interfaceName);
        var interfaceType = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.FromDefinition(interfaceDefinition),
            [comparedType]);
        return relationships.Classify(comparedType, interfaceType).IsAssignmentCompatible;
    }

    private static Selection NonGeneric(string comparerName) =>
        new(comparerName, []);

    private static Selection Generic(string comparerName, CliTypeIdentity argument) =>
        new(comparerName, [argument]);

    private sealed record Selection(
        string ComparerName,
        ImmutableArray<CliTypeIdentity> TypeArguments);
}
