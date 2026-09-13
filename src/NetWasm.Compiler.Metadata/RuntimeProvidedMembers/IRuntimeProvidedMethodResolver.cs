using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

internal interface IRuntimeProvidedMethodResolver
{
    MethodInstanceModel? Resolve(RuntimeProvidedMethodRequest request);
}
