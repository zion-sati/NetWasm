using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IFinalizerResolver
{
    MethodInstanceModel? Resolve(CliTypeIdentity allocatedType);
}
