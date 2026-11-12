using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface IIntrinsicAddressBodyPlanner
{
    ReachableMethodRequest? Plan(MethodInstanceModel method);
}

// Call sites expand a runtime intrinsic in place, so it has no function of its own. A delegate
// or function pointer to it needs one: a body that forwards its arguments to the intrinsic, whose
// call site expands it, so an indirect call does exactly what a call does. CoreLib's body for an
// intrinsic is never used: many are stand-ins that only call sites replace (issue 223's class).
internal sealed class IntrinsicAddressBodyPlanner(IRuntimeIntrinsicRegistry intrinsics) : IIntrinsicAddressBodyPlanner
{
    private readonly IRuntimeIntrinsicRegistry _intrinsics =
        intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));

    public ReachableMethodRequest? Plan(MethodInstanceModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!_intrinsics.TryGetIntrinsic(method.Definition.Key, out _))
        {
            return null;
        }
        var arguments = method.Definition.WasmParameterTypes.Length;
        var instructions = ImmutableArray.CreateBuilder<CilInstruction>(arguments + 2);
        for (var index = 0; index < arguments; index++)
        {
            Emit(CilOperation.LoadArgument, new CilOperand.Index(index));
        }
        Emit(CilOperation.Call, new CilOperand.MethodInstance(method));
        Emit(CilOperation.Return, new CilOperand.None());
        return new(method)
        {
            RewrittenBody = new(method.Definition, Math.Max(arguments, 1), [], instructions.ToImmutable())
            {
                MethodInstance = method,
            },
        };

        void Emit(CilOperation operation, CilOperand operand) =>
            instructions.Add(new CilInstruction(instructions.Count, instructions.Count + 1, operation, operand));
    }
}
