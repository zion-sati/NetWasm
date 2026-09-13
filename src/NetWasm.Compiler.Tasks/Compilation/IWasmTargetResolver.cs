using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tasks.Compilation;

internal interface IWasmTargetResolver
{
    WasmTarget Resolve(string target);
}
