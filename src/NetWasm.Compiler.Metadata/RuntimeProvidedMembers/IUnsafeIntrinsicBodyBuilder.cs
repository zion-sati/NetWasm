using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

internal interface IUnsafeIntrinsicBodyBuilder
{
    // The body the compiler gives the Unsafe intrinsic, or null for any other method.
    CilMethodBody? Build(MethodInstanceModel method);
}
