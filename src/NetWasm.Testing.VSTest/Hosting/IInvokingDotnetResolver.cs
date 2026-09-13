using NetWasm.Testing.VSTest.Configuration;

namespace NetWasm.Testing.VSTest.Hosting;

internal interface IInvokingDotnetResolver
{
    string Resolve(NetWasmRunConfiguration configuration);
}
