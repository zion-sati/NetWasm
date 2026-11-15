// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed class StructuredMethodValidator(
    IStructuredInstructionContractValidator instructions,
    IStructuredBlockOwnershipValidator ownership,
    IStructuredTargetValidator targets,
    IStructuredExceptionValidator exceptions,
    IStructuredStackContractValidator stacks) : IStructuredMethodValidator
{
    private readonly IStructuredInstructionContractValidator _instructions =
        instructions ?? throw new ArgumentNullException(nameof(instructions));
    private readonly IStructuredBlockOwnershipValidator _ownership =
        ownership ?? throw new ArgumentNullException(nameof(ownership));
    private readonly IStructuredTargetValidator _targets =
        targets ?? throw new ArgumentNullException(nameof(targets));
    private readonly IStructuredExceptionValidator _exceptions =
        exceptions ?? throw new ArgumentNullException(nameof(exceptions));
    private readonly IStructuredStackContractValidator _stacks =
        stacks ?? throw new ArgumentNullException(nameof(stacks));

    public void Validate(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        _instructions.Validate(method);
        _ownership.Validate(method);
        _targets.Validate(method);
        _exceptions.Validate(method);
        _stacks.Validate(method);
    }
}
