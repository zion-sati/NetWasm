using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ImplicitExceptionDiscovery(
    IMethodRepository methods,
    ISymbolFormatter symbols,
    IStringConstructionExceptionRequirementProvider stringConstruction,
    IRuntimeIntrinsicExceptionRequirementProvider runtimeIntrinsics) :
    IImplicitExceptionDiscovery
{
    public ImmutableArray<ReachabilityExceptionRequirement> Discover(
        CilInstruction instruction)
    {
        var requirements = ImmutableArray.CreateBuilder<ReachabilityExceptionRequirement>();
        switch (instruction.Operation)
        {
            case CilOperation.NewObject:
                var constructor = instruction.Operand switch
                {
                    CilOperand.Entity target => methods.GetMethod(target.Key),
                    CilOperand.MethodInstance target => target.Value.Definition,
                    _ => null,
                };
                if (constructor is not null &&
                    symbols.Format(constructor.DeclaringType) == "System.String")
                {
                    requirements.AddRange(stringConstruction.Discover(constructor));
                }
                else
                {
                    requirements.Add(new(
                        ManagedExceptionKind.OutOfMemory,
                        "System.OutOfMemoryException"));
                }
                break;
            case CilOperation.Box:
                requirements.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
                break;
            case CilOperation.Unbox:
            case CilOperation.UnboxAny:
                requirements.Add(new(ManagedExceptionKind.NullReference, "System.NullReferenceException"));
                requirements.Add(new(ManagedExceptionKind.InvalidCast, "System.InvalidCastException"));
                break;
            case CilOperation.NewArray:
            case CilOperation.NewRectangularArray:
            case CilOperation.NewBoundedRectangularArray:
                requirements.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
                requirements.Add(new(ManagedExceptionKind.Overflow, "System.OverflowException"));
                if (instruction.Operation == CilOperation.NewBoundedRectangularArray)
                {
                    requirements.Add(new(
                        ManagedExceptionKind.ArgumentOutOfRange, "System.ArgumentOutOfRangeException"));
                }
                break;
            case CilOperation.LoadField:
            case CilOperation.StoreField:
            case CilOperation.LoadArrayLength:
                requirements.Add(new(ManagedExceptionKind.NullReference, "System.NullReferenceException"));
                break;
            case CilOperation.CallVirtual:
                requirements.Add(new(ManagedExceptionKind.NullReference, "System.NullReferenceException"));
                requirements.Add(new(ManagedExceptionKind.InvalidCast, "System.InvalidCastException"));
                var virtualMethod = ResolveCalledMethod(instruction.Operand);
                if (virtualMethod is not null)
                {
                    requirements.AddRange(runtimeIntrinsics.Discover(virtualMethod));
                }
                if (virtualMethod is not null &&
                    symbols.Format(virtualMethod.DeclaringType) == "System.String" &&
                    virtualMethod.Name == "get_Chars")
                {
                    requirements.Add(new(
                        ManagedExceptionKind.IndexOutOfRange,
                        "System.IndexOutOfRangeException"));
                }
                if (virtualMethod is not null &&
                    symbols.Format(virtualMethod.DeclaringType) == "System.Array" &&
                    virtualMethod.Name == "GetLength")
                {
                    requirements.Add(new(
                        ManagedExceptionKind.IndexOutOfRange,
                        "System.IndexOutOfRangeException"));
                }
                break;
            case CilOperation.LoadArrayElementReference:
            case CilOperation.LoadArrayElement:
            case CilOperation.LoadArrayElementAddress:
            case CilOperation.LoadRectangularArrayElement:
                requirements.Add(new(ManagedExceptionKind.NullReference, "System.NullReferenceException"));
                requirements.Add(new(
                    ManagedExceptionKind.IndexOutOfRange,
                    "System.IndexOutOfRangeException"));
                break;
            case CilOperation.StoreArrayElementReference:
            case CilOperation.StoreArrayElement:
            case CilOperation.StoreRectangularArrayElement:
            case CilOperation.LoadRectangularArrayElementAddress:
                requirements.Add(new(ManagedExceptionKind.NullReference, "System.NullReferenceException"));
                requirements.Add(new(
                    ManagedExceptionKind.IndexOutOfRange,
                    "System.IndexOutOfRangeException"));
                requirements.Add(new(
                    ManagedExceptionKind.ArrayTypeMismatch,
                    "System.ArrayTypeMismatchException"));
                break;
            case CilOperation.Divide:
            case CilOperation.DivideUnsigned:
            case CilOperation.Remainder:
            case CilOperation.RemainderUnsigned:
                requirements.Add(new(ManagedExceptionKind.DivideByZero, "System.DivideByZeroException"));
                if (instruction.Operation != CilOperation.Remainder &&
                    instruction.Operation != CilOperation.RemainderUnsigned)
                {
                    requirements.Add(new(ManagedExceptionKind.Overflow, "System.OverflowException"));
                }
                break;
            case CilOperation.AddChecked:
            case CilOperation.AddCheckedUnsigned:
            case CilOperation.SubtractChecked:
            case CilOperation.SubtractCheckedUnsigned:
            case CilOperation.MultiplyChecked:
            case CilOperation.MultiplyCheckedUnsigned:
                requirements.Add(new(ManagedExceptionKind.Overflow, "System.OverflowException"));
                break;
            case CilOperation.ConvertNumeric when
                instruction.Operand is CilOperand.NumericConversion { Checked: true }:
                requirements.Add(new(ManagedExceptionKind.Overflow, "System.OverflowException"));
                break;
            case CilOperation.CheckFinite:
                requirements.Add(new(ManagedExceptionKind.Arithmetic, "System.ArithmeticException"));
                break;
            case CilOperation.CastClass:
            case CilOperation.CallIndirect:
                requirements.Add(new(ManagedExceptionKind.InvalidCast, "System.InvalidCastException"));
                break;
            case CilOperation.DelegateCombine:
            case CilOperation.DelegateRemove:
                requirements.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
                requirements.Add(new(ManagedExceptionKind.Argument, "System.ArgumentException"));
                break;
            case CilOperation.MaterializeType:
                requirements.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
                break;
            case CilOperation.GetObjectType:
                requirements.Add(new(ManagedExceptionKind.NullReference, "System.NullReferenceException"));
                requirements.Add(new(ManagedExceptionKind.OutOfMemory, "System.OutOfMemoryException"));
                break;
            case CilOperation.Throw:
                requirements.Add(new(ManagedExceptionKind.NullReference, "System.NullReferenceException"));
                break;
            case CilOperation.Call:
                var called = ResolveCalledMethod(instruction.Operand);
                if (called is null)
                {
                    break;
                }
                requirements.AddRange(runtimeIntrinsics.Discover(called));
                break;
        }
        return requirements.Distinct().ToImmutableArray();

        MethodDefinitionModel? ResolveCalledMethod(CilOperand operand) => operand switch
        {
            CilOperand.Entity target => methods.GetMethod(target.Key),
            CilOperand.MethodInstance target => target.Value.Definition,
            _ => null,
        };
    }
}
