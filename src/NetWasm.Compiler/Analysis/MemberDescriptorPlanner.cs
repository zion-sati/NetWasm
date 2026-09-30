using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class MemberDescriptorPlanner(
    IMetadataPropertyAccessorResolver propertyAccessors) : IMemberDescriptorPlanner
{
    private readonly IMetadataPropertyAccessorResolver _propertyAccessors =
        propertyAccessors ?? throw new ArgumentNullException(nameof(propertyAccessors));

    public MemberDescriptorPlan Build(MemberDescriptorPlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var methods = request.MethodDescriptors.ToBuilder();
        var fields = request.FieldDescriptors.ToBuilder();
        var properties = ImmutableDictionary.CreateBuilder<
            string,
            PropertyInstanceModel>(StringComparer.Ordinal);
        var types = ImmutableHashSet.CreateBuilder<CliTypeIdentity>();
        var namedDescriptors = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var nameStrings = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var pending = new Queue<MethodInstanceModel>(methods.Values);

        while (pending.TryDequeue(out var method))
        {
            AddMethodFacts(method);
            if (!request.IncludePropertyAssociations)
            {
                continue;
            }

            var property = _propertyAccessors.Resolve(method);
            if (property is null)
            {
                continue;
            }
            AddProperty(property);
            AddAccessor(property.Getter);
            AddAccessor(property.Setter);
        }

        foreach (var field in fields.Values)
        {
            AddType(field.DeclaringType);
            AddType(field.FieldType);
            if (request.IncludeNames)
            {
                namedDescriptors.Add(field.CanonicalName);
                nameStrings.Add(field.Definition.Name);
            }
        }

        return new(
            methods.ToImmutable(),
            fields.ToImmutable(),
            properties.ToImmutable(),
            types.ToImmutable(),
            namedDescriptors.ToImmutable(),
            nameStrings.ToImmutable());

        void AddMethodFacts(MethodInstanceModel method)
        {
            AddType(method.DeclaringType);
            AddType(method.Signature.ReturnSignatureType);
            foreach (var parameter in method.Signature.ParameterSignatureTypes)
            {
                AddType(parameter);
            }
            foreach (var argument in method.MethodArguments)
            {
                AddType(argument);
            }
            if (request.IncludeNames)
            {
                namedDescriptors.Add(method.CanonicalName);
                nameStrings.Add(method.Definition.Name);
            }
        }

        void AddProperty(PropertyInstanceModel property)
        {
            if (properties.TryGetValue(property.CanonicalName, out var existing))
            {
                if (!existing.HasEquivalentDescriptorFacts(property))
                {
                    throw ContradictoryProperty(property.CanonicalName);
                }
                return;
            }
            properties.Add(property.CanonicalName, property);
            AddType(property.DeclaringType);
            AddType(property.PropertyType);
            foreach (var parameter in property.IndexParameterTypes)
            {
                AddType(parameter);
            }
            if (request.IncludeNames)
            {
                namedDescriptors.Add(property.CanonicalName);
                nameStrings.Add(property.Definition.Name);
            }
        }

        void AddAccessor(MethodInstanceModel? accessor)
        {
            if (accessor is null)
            {
                return;
            }
            if (methods.TryGetValue(accessor.CanonicalName, out var existing))
            {
                if (!existing.HasEquivalentDescriptorFacts(accessor))
                {
                    throw new CompilerException(new CompilerDiagnostic(
                        DiagnosticCode.RuntimeContract,
                        $"member descriptor '{accessor.CanonicalName}' has contradictory methods"));
                }
                return;
            }
            methods.Add(accessor.CanonicalName, accessor);
            pending.Enqueue(accessor);
        }

        void AddType(CliTypeIdentity type)
        {
            if (!types.Add(type))
            {
                return;
            }
            if (type.ElementType is not null)
            {
                AddType(type.ElementType);
            }
            foreach (var argument in type.TypeArguments)
            {
                AddType(argument);
            }
        }
    }

    private static CompilerException ContradictoryProperty(string identity) => new(
        new CompilerDiagnostic(
            DiagnosticCode.RuntimeContract,
            $"member descriptor '{identity}' has contradictory properties"));
}
