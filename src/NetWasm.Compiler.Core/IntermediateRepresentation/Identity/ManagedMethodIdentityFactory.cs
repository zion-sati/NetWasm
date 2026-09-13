using System;

namespace NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

public sealed class ManagedMethodIdentityFactory : IManagedMethodIdentityFactory
{
    public ManagedMethodIdentity Create(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);

        return new ManagedMethodIdentity(method.CanonicalName);
    }

    public ManagedMethodIdentity Create(MethodDefinitionModel method, CliTypeIdentity declaringType)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(declaringType);

        return Create(new MethodInstanceModel(method, declaringType, [], method.Signature));
    }
}
