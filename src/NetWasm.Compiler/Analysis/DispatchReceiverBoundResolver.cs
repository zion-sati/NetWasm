using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Analysis;

internal interface IDispatchReceiverBoundResolver
{
    CliTypeIdentity? Resolve(ReachabilityInstructionRequest request, int callIndex, MethodInstanceModel declaration);
}

// The static type every receiver of a virtual call is assignable to, where
// the IL states it: the type a constrained prefix names, or, for a call that
// takes no arguments, the declared type of what the instruction before it
// pushes. A foreach over IEnumerable<T> keeps its enumerator in a local of
// type IEnumerator<T>, so its MoveNext and Dispose reach only enumerators of
// T rather than every IEnumerator and IDisposable in the program.
internal sealed class DispatchReceiverBoundResolver(ITypeOperandResolver typeOperands) : IDispatchReceiverBoundResolver
{
    public CliTypeIdentity? Resolve(ReachabilityInstructionRequest request, int callIndex, MethodInstanceModel declaration)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(declaration);
        if (callIndex <= 0)
        {
            return null;
        }

        var previous = request.Body.Instructions[callIndex - 1];
        var bound = previous.Operation == CilOperation.Constrained
            ? ((CilOperand.TypeIdentity)previous.Operand).Value
            // Without arguments the receiver is the value the previous
            // instruction pushes, unless control also enters the call elsewhere.
            : declaration.Signature.ParameterSignatureTypes.IsEmpty &&
              !IsEntered(request.Body, request.Body.Instructions[callIndex].Offset)
                ? Pushed(request, previous)
                : null;
        return bound is { IsValueType: false, ContainsGenericParameters: false } &&
            bound.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation or
                CliTypeShape.SzArray or CliTypeShape.Array
            ? bound
            : null;
    }

    private CliTypeIdentity? Pushed(ReachabilityInstructionRequest request, CilInstruction instruction) =>
        (instruction.Operation, instruction.Operand) switch
        {
            (CilOperation.LoadLocal, CilOperand.Index local) when
                (uint)local.Value < (uint)request.Body.LocalSignatureTypes.Length =>
                request.Body.LocalSignatureTypes[local.Value],
            (CilOperation.LoadArgument, CilOperand.Index argument) => Argument(request.Method, argument.Value),
            (CilOperation.LoadField or CilOperation.LoadStaticField, CilOperand.FieldInstance field) =>
                field.Value.FieldType,
            (CilOperation.CastClass or CilOperation.IsInstance, _) =>
                typeOperands.Resolve(instruction, request.Method),
            // A constructed object has exactly the constructor's type.
            (CilOperation.NewObject, CilOperand.MethodInstance constructor) => constructor.Value.DeclaringType,
            _ => null,
        };

    private static CliTypeIdentity? Argument(MethodInstanceModel method, int index)
    {
        if (!method.Definition.IsStatic)
        {
            if (index == 0)
            {
                return method.DeclaringType;
            }
            index--;
        }
        return (uint)index < (uint)method.Signature.ParameterSignatureTypes.Length
            ? method.Signature.ParameterSignatureTypes[index]
            : null;
    }

    private static bool IsEntered(CilMethodBody body, int offset) =>
        body.Instructions.Any(instruction => instruction.Operand switch
        {
            CilOperand.BranchTarget target => target.Offset == offset,
            CilOperand.SwitchTargets targets => targets.Offsets.Contains(offset),
            _ => false,
        }) ||
        body.ExceptionRegions.Any(region => region.HandlerOffset == offset || region.FilterOffset == offset);
}
