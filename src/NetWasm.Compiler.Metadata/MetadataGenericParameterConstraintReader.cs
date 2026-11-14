using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.GenericConstraints;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataGenericParameterConstraintReader : IGenericParameterConstraintReader
{
    private readonly ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot> _assemblies;
    private readonly ITypeDefinitionResolver _definitions;
    private readonly ConcurrentDictionary<EntityKey, ImmutableArray<GenericParameterConstraintModel>> _parameters = [];

    public MetadataGenericParameterConstraintReader(MetadataCompilationSnapshot metadata, ITypeDefinitionResolver definitions)
        : this((metadata ?? throw new ArgumentNullException(nameof(metadata))).Assemblies
            .ToImmutableDictionary(assembly => assembly.Identity, assembly => assembly.Metadata), definitions)
    {
    }

    internal MetadataGenericParameterConstraintReader(
        ImmutableDictionary<AssemblyIdentity, MetadataAssemblySnapshot> assemblies, ITypeDefinitionResolver definitions)
    {
        _assemblies = assemblies ?? throw new ArgumentNullException(nameof(assemblies));
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    }

    public ImmutableArray<GenericParameterConstraintModel> Read(EntityKey owner) => _parameters.GetOrAdd(owner, ReadCore);

    private ImmutableArray<GenericParameterConstraintModel> ReadCore(EntityKey owner)
    {
        if (!_assemblies.TryGetValue(owner.Assembly, out var source))
            throw new CompilerException(new(DiagnosticCode.AssemblyResolution,
                $"Generic constraint owner assembly '{owner.Assembly.Name}' is not in the compilation unit."));
        var reader = source.Reader;
        var handle = MetadataTokens.EntityHandle(owner.MetadataToken);
        var handles = handle.Kind switch
        {
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetGenericParameters(),
            HandleKind.TypeDefinition => reader.GetTypeDefinition((TypeDefinitionHandle)handle).GetGenericParameters(),
            _ => throw new ArgumentException("A type or method definition is required.", nameof(owner)),
        };
        var provider = new SignatureTypeProvider(source.Identity, reader, source.AssemblyIdentityAliases);
        var result = ImmutableArray.CreateBuilder<GenericParameterConstraintModel>(handles.Count);
        foreach (var parameterHandle in handles)
        {
            var parameter = reader.GetGenericParameter(parameterHandle);
            if (parameter.Index != result.Count)
                throw new CompilerException(new(DiagnosticCode.UnsupportedMetadata,
                    "Generic parameter indices must be contiguous and ordered."));
            var constraints = ImmutableArray.CreateBuilder<CliTypeIdentity>();
            foreach (var constraintHandle in parameter.GetConstraints())
            {
                var type = reader.GetGenericParameterConstraint(constraintHandle).Type;
                constraints.Add(type.Kind switch
                {
                    HandleKind.TypeDefinition => Canonical(provider.GetTypeFromDefinition(reader, (TypeDefinitionHandle)type, 0)),
                    HandleKind.TypeReference => Canonical(provider.GetTypeFromReference(reader, (TypeReferenceHandle)type, 0)),
                    _ => provider.GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)type, 0),
                });
            }
            result.Add(new(parameter.Attributes, constraints.ToImmutable()));
        }
        return result.MoveToImmutable();
    }

    private CliTypeIdentity Canonical(CliTypeIdentity identity) =>
        CliTypeIdentity.FromDefinition(_definitions.ResolveTypeIdentity(identity));
}
