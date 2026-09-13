using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

public interface IManagedMethodIdentityFactory
{
    ManagedMethodIdentity Create(MethodInstanceModel method);

    ManagedMethodIdentity Create(MethodDefinitionModel method, CliTypeIdentity declaringType);
}
