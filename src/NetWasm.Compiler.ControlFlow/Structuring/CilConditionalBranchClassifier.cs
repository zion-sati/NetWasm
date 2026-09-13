using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structuring;

internal sealed class CilConditionalBranchClassifier : ICilConditionalBranchClassifier
{
    public bool Classify(CilOperation operation) => operation is
        CilOperation.BranchIfTrue or CilOperation.BranchIfFalse or
        CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual or
        CilOperation.BranchIfGreaterThanSigned or CilOperation.BranchIfGreaterThanUnsigned or
        CilOperation.BranchIfGreaterThanOrEqualSigned or
        CilOperation.BranchIfGreaterThanOrEqualUnsigned or
        CilOperation.BranchIfLessThanSigned or CilOperation.BranchIfLessThanUnsigned or
        CilOperation.BranchIfLessThanOrEqualSigned or
        CilOperation.BranchIfLessThanOrEqualUnsigned;
}
