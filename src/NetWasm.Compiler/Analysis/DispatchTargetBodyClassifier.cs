using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// A dispatch target runs as a body unless the runtime implements it as an intrinsic.
internal sealed class DispatchTargetBodyClassifier(IRuntimeIntrinsicRegistry intrinsics) : IDispatchTargetBodyClassifier
{
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ??
        throw new ArgumentNullException(nameof(intrinsics));

    public bool RunsAsBody(MethodInstanceModel target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return !_intrinsics.TryGetIntrinsic(target.Definition.Key, out _);
    }
}
