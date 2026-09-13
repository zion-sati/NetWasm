using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IConstrainedCallTargetResolver
{
    MethodDefinitionModel Resolve(ConstrainedCallTargetRequest request);
}
