using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IDispatchTargetBodyClassifier
{
    // Whether a virtual method's dispatch target runs as a compiled body. A runtime intrinsic (Enum.ToString on a
    // boxed enum) has none, so member execution leaves it out of the targets.
    bool RunsAsBody(MethodInstanceModel target);
}
