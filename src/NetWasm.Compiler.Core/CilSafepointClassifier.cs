using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.Core;

public static class CilSafepointClassifier
{
    public static bool RequiresUnconditionalRootDecision(CilInstruction instruction) =>
        instruction.Operation is
            CilOperation.NewObject or
            CilOperation.NewArray or
            CilOperation.NewRectangularArray or
            CilOperation.NewBoundedRectangularArray or
            CilOperation.Box or
            CilOperation.DelegateCombine or
            CilOperation.DelegateRemove or
            CilOperation.MaterializeType or
            CilOperation.GetObjectType or
            CilOperation.CallIndirect;


    public static bool MayTransferControlExceptionally(CilInstruction instruction) =>
        instruction.Operation switch
        {
            CilOperation.Call or
            CilOperation.CallVirtual or
            CilOperation.CallIndirect or
            CilOperation.NewObject or
            CilOperation.NewArray or
            CilOperation.NewRectangularArray or
            CilOperation.NewBoundedRectangularArray or
            CilOperation.Box or
            CilOperation.Unbox or
            CilOperation.UnboxAny or
            CilOperation.LoadField or
            CilOperation.LoadFieldAddress or
            CilOperation.StoreField or
            CilOperation.LoadStaticField or
            CilOperation.LoadStaticFieldAddress or
            CilOperation.StoreStaticField or
            CilOperation.LoadArrayLength or
            CilOperation.LoadArrayElementReference or
            CilOperation.LoadArrayElement or
            CilOperation.LoadArrayElementAddress or
            CilOperation.StoreArrayElementReference or
            CilOperation.StoreArrayElement or
            CilOperation.LoadRectangularArrayElement or
            CilOperation.LoadRectangularArrayElementAddress or
            CilOperation.StoreRectangularArrayElement or
            CilOperation.Divide or
            CilOperation.DivideUnsigned or
            CilOperation.Remainder or
            CilOperation.RemainderUnsigned or
            CilOperation.AddChecked or
            CilOperation.AddCheckedUnsigned or
            CilOperation.SubtractChecked or
            CilOperation.SubtractCheckedUnsigned or
            CilOperation.MultiplyChecked or
            CilOperation.MultiplyCheckedUnsigned or
            CilOperation.CastClass or
            CilOperation.LoadVirtualFunction or
            CilOperation.DelegateCombine or
            CilOperation.DelegateRemove or
            CilOperation.MaterializeType or
            CilOperation.GetObjectType or
            CilOperation.Throw or
            CilOperation.Rethrow or
            CilOperation.EndFinally => true,
            CilOperation.ConvertNumeric =>
                instruction.Operand is CilOperand.NumericConversion { Checked: true },
            _ => false,
        };

}
