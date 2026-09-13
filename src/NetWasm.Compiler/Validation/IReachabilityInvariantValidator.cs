using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Validation;

internal interface IReachabilityInvariantValidator
{
    void Validate(
        IMethodInstanceResolver methodInstances,
        ITypeClassifier types,
        ReachableProgram program,
        IRuntimeIntrinsicRegistry intrinsics);
}
