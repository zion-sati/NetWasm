using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IManagedExceptionObjectProviderFactory
{
    IManagedExceptionObjectProvider Create(ManagedLayoutSnapshot snapshot);
}
