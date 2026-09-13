using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMethodFinder
{
    MethodDefinitionModel FindMethod(
        AssemblyIdentity assembly,
        string typeFullName,
        string methodName);

    MethodDefinitionModel FindMethod(
        AssemblyIdentity assembly,
        int metadataToken);
}
