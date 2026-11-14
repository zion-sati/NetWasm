using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

internal interface IUnboundNativeImportBodyBuilder
{
    CilMethodBody Build(MethodInstanceModel method);
}
