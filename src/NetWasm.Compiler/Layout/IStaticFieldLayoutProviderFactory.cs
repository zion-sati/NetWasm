using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IStaticFieldLayoutProviderFactory
{
    IStaticFieldLayoutProvider Create(ManagedLayoutSnapshot snapshot);
}
