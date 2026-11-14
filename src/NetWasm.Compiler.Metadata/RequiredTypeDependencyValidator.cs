using NetWasm.Compiler.Core.GenericConstraints;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IRequiredTypeDependencyValidator
{
    void Validate(IEnumerable<CliTypeIdentity> types);
}

public sealed class RequiredTypeDependencyValidator(
    ITypeDefinitionResolver definitions,
    IBaseTypeIdentityResolver bases,
    IImplementedInterfaceResolver interfaces,
    IClosedGenericConstraintValidator constraints) : IRequiredTypeDependencyValidator
{
    private readonly ITypeDefinitionResolver _definitions = definitions ??
        throw new ArgumentNullException(nameof(definitions));
    private readonly IBaseTypeIdentityResolver _bases = bases ??
        throw new ArgumentNullException(nameof(bases));
    private readonly IImplementedInterfaceResolver _interfaces = interfaces ??
        throw new ArgumentNullException(nameof(interfaces));

    private readonly IClosedGenericConstraintValidator _constraints = constraints ?? throw new ArgumentNullException(nameof(constraints));

    public void Validate(IEnumerable<CliTypeIdentity> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var pending = new Stack<(CliTypeIdentity Type, CompilerDiagnosticContext? Source, ImmutableStack<CompilerDependencyStep> Chain, bool RequiresInheritance)>();
        foreach (var type in types)
            pending.Push((type, null, ImmutableStack<CompilerDependencyStep>.Empty, false));
        var sealedTypes = new Dictionary<CliTypeIdentity, bool>();
        while (pending.TryPop(out var item))
        {
            var (type, source, chain, requiresInheritance) = item;
            var requirement = CompilerDependencyKind.TypeStructure;
            try
            {
                TypeDefinitionModel? definition = null;
                var visited = sealedTypes.TryGetValue(type, out var isSealed);
                if (!visited)
                {
                    if (type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation)
                    {
                        definition = _definitions.ResolveTypeIdentity(type);
                        isSealed = definition.IsSealed;
                    }
                    sealedTypes.Add(type, isSealed);
                }
                // Validate the relationship even if this type was already seen
                // as an ordinary requirement. A sealed type is usable, not inheritable.
                // Only a base type requires inheritance, and its derived type is its source.
                if (requiresInheritance && isSealed)
                    throw new CompilerException(new(DiagnosticCode.UnsupportedMetadata,
                        $"type '{source!.InstanceIdentity}' cannot inherit from sealed type '{type.CanonicalName}'"));
                if (visited)
                    continue;
                if (definition is not null)
                {
                    source = new(definition.Key, type.ToString());
                    _constraints.Validate(type);
                    requirement = CompilerDependencyKind.BaseType;
                    if (_bases.GetBaseTypeIdentity(type) is { } baseType)
                        pending.Push((baseType, source, chain.Push(new(source, CompilerDependencyKind.BaseType)), true));
                    requirement = CompilerDependencyKind.Interface;
                    foreach (var implemented in _interfaces.GetInterfaces(type))
                        pending.Push((implemented, source, chain.Push(new(source, CompilerDependencyKind.Interface)), false));
                }
                else if (type.ElementType is { } element)
                {
                    pending.Push((element, source, chain, false));
                }
                // Only a generic instantiation has type arguments, and it has a definition, its source.
                foreach (var argument in type.TypeArguments)
                    pending.Push((argument, source, chain.Push(new(source!, CompilerDependencyKind.TypeArgument)), false));
            }
            catch (CompilerException failure) when (failure.Diagnostic.Context is null && source is not null)
            {
                if (chain.IsEmpty || chain.Peek().Source != source)
                    chain = chain.Push(new(source, requirement));
                throw new CompilerException(failure.Diagnostic with
                {
                    Context = source,
                    DependencyChain = failure.Diagnostic.DependencyChain.IsDefaultOrEmpty
                        ? chain.Reverse().ToImmutableArray()
                        : failure.Diagnostic.DependencyChain,
                }, failure);
            }
        }
    }
}
