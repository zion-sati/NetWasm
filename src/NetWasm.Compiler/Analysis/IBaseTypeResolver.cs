using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IBaseTypeResolver
{
    CliTypeIdentity? Resolve(CliTypeIdentity type);
}
