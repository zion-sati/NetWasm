using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IFunctionIndexResolver
{
    int Resolve(EntityKey method);

    int Resolve(string method);

    int Resolve(MethodInstanceModel method);
}
