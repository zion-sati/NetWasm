using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

// Whether the type initializer an access or call may run can collect.
internal static class TypeInitializerAllocation
{
    public static bool IsAllocating(
        ITypeRepository types,
        IMethodRepository methods,
        EntityKey typeDefinition,
        CliTypeIdentity? declaringType,
        ISet<EntityKey> allocatingMethods,
        ISet<string> allocatingConstructedMethods)
    {
        var initializer = types.GetTypeDefinition(typeDefinition)
            .Methods.Select(methods.GetMethod)
            .SingleOrDefault(method => method.Name == ".cctor");
        if (initializer is null)
            return false;
        // Allocation capabilities include the failure boundary of every
        // reachable initializer, even if its managed body does not allocate.
        if (declaringType is { Shape: CliTypeShape.GenericInstantiation })
        {
            var canonicalName =
                $"{declaringType.CanonicalName}::" +
                $"0x{initializer.Key.MetadataToken:x8}";
            return allocatingConstructedMethods.Contains(canonicalName);
        }
        return allocatingMethods.Contains(initializer.Key);
    }
}
