using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.ExceptionTypes;

public interface IReachableExceptionTypePlanner
{
    IReadOnlyList<ReachableExceptionType> Plan(
        MetadataCompilationSnapshot metadata,
        ITypeIdentityResolver identities,
        IAssemblyIdentityFormatter assemblies,
        ITypeDescriptorSource descriptors);
}

public sealed class ReachableExceptionTypePlanner(
    IReachableExceptionTypeValidator validator) : IReachableExceptionTypePlanner
{
    public IReadOnlyList<ReachableExceptionType> Plan(
        MetadataCompilationSnapshot metadata,
        ITypeIdentityResolver identities,
        IAssemblyIdentityFormatter assemblies,
        ITypeDescriptorSource descriptors)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(descriptors);
        var definitionDescriptors = descriptors.TypeDescriptors.ToDictionary(
            descriptor => descriptor.TypeId);
        var constructedDescriptors = descriptors.ConstructedTypeDescriptors.ToDictionary(
            descriptor => descriptor.TypeId);
        var exceptionTypeId = definitionDescriptors.Values
            .Where(descriptor => identities.GetTypeIdentity(descriptor.Type).FullName ==
                                 "System.Exception")
            .Select(descriptor => descriptor.TypeId)
            .SingleOrDefault();
        if (exceptionTypeId == 0)
        {
            return [];
        }

        bool IsExceptionType(int typeId)
        {
            var visited = new HashSet<int>();
            while (typeId != 0 && visited.Add(typeId))
            {
                if (typeId == exceptionTypeId)
                {
                    return true;
                }

                if (definitionDescriptors.TryGetValue(typeId, out var definition))
                {
                    typeId = definition.BaseTypeId;
                }
                else if (constructedDescriptors.TryGetValue(typeId, out var constructed))
                {
                    typeId = constructed.BaseTypeId;
                }
                else
                {
                    return false;
                }
            }

            return false;
        }

        var entries = new List<ReachableExceptionType>();
        foreach (var descriptor in definitionDescriptors.Values.Where(
                     descriptor => IsExceptionType(descriptor.TypeId)))
        {
            var identity = identities.GetTypeIdentity(descriptor.Type);
            var assembly = assemblies.Format(descriptor.Type.Assembly);
            var displayName = identity.FullName ?? identity.CanonicalName;
            entries.Add(new(
                descriptor.TypeId,
                displayName + ", " + assembly,
                displayName,
                assembly));
        }

        foreach (var descriptor in constructedDescriptors.Values.Where(
                     descriptor => IsExceptionType(descriptor.TypeId)))
        {
            var identity = descriptor.Type;
            var assemblyIdentity = ResolveDeclaringAssembly(identity);
            var assembly = assemblies.Format(assemblyIdentity);
            entries.Add(new(
                descriptor.TypeId,
                identity.CanonicalName + ", " + assembly,
                identity.CanonicalName,
                assembly));
        }

        return validator.Validate(entries);
    }

    internal static AssemblyIdentity ResolveDeclaringAssembly(CliTypeIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var current = identity;
        while (true)
        {
            if (current.Assembly is { } assembly)
            {
                return assembly;
            }

            current = current.ElementType
                ?? throw new InvalidOperationException(
                    $"Constructed exception type '{identity.CanonicalName}' has no " +
                    "declaring assembly identity.");
        }
    }
}
