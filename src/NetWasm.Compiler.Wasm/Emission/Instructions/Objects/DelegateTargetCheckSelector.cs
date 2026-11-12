using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface IDelegateTargetCheckSelector
{
    bool Select(InstructionEmissionRequest request);
}

// Strategy: ldftn of an instance method just before newobj closes the delegate over its target
// (ECMA-335 II.14.6.1), which must not be null; desktop .NET throws ArgumentException when it is,
// a delegate converted from a null delegate included. dup; ldvirtftn has already thrown
// NullReferenceException, and a static method may close over null.
internal sealed class DelegateTargetCheckSelector(
    ISolePredecessorResolver predecessors,
    IMethodRepository methods) : IDelegateTargetCheckSelector
{
    public bool Select(InstructionEmissionRequest request) =>
        predecessors.Resolve(request) is { Operation: CilOperation.LoadFunction } load && !IsStatic(load.Operand);

    private bool IsStatic(CilOperand operand) => operand switch
    {
        CilOperand.MethodInstance method => method.Value.Definition.IsStatic,
        CilOperand.Entity method => methods.GetMethod(method.Key).IsStatic,
        _ => throw new InvalidOperationException($"ldftn names no method: {operand}"),
    };
}
