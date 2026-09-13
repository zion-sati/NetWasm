using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IMethodBodyReader
{
    CilMethodBody ReadMethodBody(MethodDefinitionModel method);
    CilMethodBody ReadMethodBody(MethodInstanceModel method);
}
