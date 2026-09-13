using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IValueLayoutProviderFactory
{
    IValueLayoutProvider Create(IValueLayoutResolver resolver);
}
