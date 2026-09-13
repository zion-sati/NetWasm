using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class ManagedExceptionObjectProviderFactory :
    IManagedExceptionObjectProviderFactory
{
    public IManagedExceptionObjectProvider Create(ManagedLayoutSnapshot snapshot) =>
        new ManagedExceptionObjectProvider(snapshot);
}
