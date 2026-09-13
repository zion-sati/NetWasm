using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IDispatchTargetResolver
{
    DispatchTargetModel? Resolve(
        MethodInstanceModel declaration,
        CliTypeIdentity receiver);
}
