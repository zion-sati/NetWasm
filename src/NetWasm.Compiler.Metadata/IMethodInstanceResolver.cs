using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMethodInstanceResolver
{
    MethodInstanceModel ResolveMethodInstance(
        AssemblyIdentity source,
        int metadataToken,
        string methodDisplayName,
        int ilOffset,
        CliGenericContext? genericContext = null);
}
