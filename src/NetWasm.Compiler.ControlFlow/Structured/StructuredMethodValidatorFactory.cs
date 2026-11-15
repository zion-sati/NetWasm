// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ControlFlow.Structured;

public sealed class StructuredMethodValidatorFactory : IStructuredMethodValidatorFactory
{
    public IStructuredMethodValidator Create() => new StructuredMethodValidator(
        new StructuredInstructionContractValidator(),
        new StructuredBlockOwnershipValidator(),
        new StructuredTargetValidator(),
        new StructuredExceptionValidator(),
        new StructuredStackContractValidator());
}
