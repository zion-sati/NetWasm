using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IMemberDescriptorLayoutProviderFactory
{
    IMemberDescriptorLayout Create(ManagedLayoutSnapshot snapshot);
}
