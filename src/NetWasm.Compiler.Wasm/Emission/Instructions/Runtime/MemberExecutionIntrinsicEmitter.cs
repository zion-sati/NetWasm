// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Instructions.Dispatch;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.MemberExecution;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

// Bounded member execution: the descriptor selects one planned member's case (a method run, a field read or a
// field write); a descriptor no case takes calls the managed fallback, which throws.
internal sealed class MemberExecutionIntrinsicEmitter(
    IMemberExecutionPlanValidator validator,
    IRootPublicationEmitter roots,
    IMemberMethodCaseEmitter methods,
    IMemberFieldReadCaseEmitter fieldReads,
    IMemberFieldWriteCaseEmitter fieldWrites,
    IKeyedDispatchEmitter keyedDispatch,
    ITargetLayout layouts) : IRuntimeIntrinsicEmitter
{
    private readonly IMemberExecutionPlanValidator _validator = validator ??
        throw new ArgumentNullException(nameof(validator));
    private readonly IRootPublicationEmitter _roots = roots ??
        throw new ArgumentNullException(nameof(roots));
    private readonly IMemberMethodCaseEmitter _methods = methods ??
        throw new ArgumentNullException(nameof(methods));
    private readonly IMemberFieldReadCaseEmitter _fieldReads = fieldReads ??
        throw new ArgumentNullException(nameof(fieldReads));
    private readonly IMemberFieldWriteCaseEmitter _fieldWrites = fieldWrites ??
        throw new ArgumentNullException(nameof(fieldWrites));
    private readonly IKeyedDispatchEmitter _keyedDispatch = keyedDispatch ??
        throw new ArgumentNullException(nameof(keyedDispatch));
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        _validator.Validate(request);
        MemberExecutionPlan plan = request.Instruction.Target.MemberExecution;
        _roots.Emit(request.Instruction, code);
        var cases = request.Intrinsic switch
        {
            RuntimeIntrinsic.MemberExecuteMethod => _methods.Cases(request, code, plan),
            RuntimeIntrinsic.MemberReadField => _fieldReads.Cases(request, code, plan),
            _ => _fieldWrites.Cases(request, code, plan),
        };
        // The descriptor names the member: its address picks the case, searched among the members' descriptors, and
        // a member the program does not plan for goes to the unsupported target.
        var ordered = cases.OrderBy(memberCase => memberCase.DescriptorAddress).ToArray();
        var descriptorLocal = request.Local(0, CliValueKind.ManagedReference);
        _keyedDispatch.Emit(code, new(
            descriptorLocal,
            _layouts.Target.UsesMemory64,
            [.. ordered.Select(memberCase => new KeyRange(memberCase.DescriptorAddress, memberCase.DescriptorAddress))],
            index => ordered[index].Emit(),
            () =>
            {
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.Call,
                    WasmInstructionOperand.Unsigned(
                        (uint)request.FunctionIndices.Resolve(plan.UnsupportedTarget!))));
                code.Write(WasmInstruction.WithOperand(
                    WasmOpcodes.LocalSet,
                    WasmInstructionOperand.Unsigned((uint)descriptorLocal)));
            }));
    }
}
