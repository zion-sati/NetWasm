using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class MemberDescriptorLayoutProviderFactory : IMemberDescriptorLayoutProviderFactory
{
    public IMemberDescriptorLayout Create(ManagedLayoutSnapshot snapshot) =>
        new MemberDescriptorLayoutProvider(snapshot);
}
