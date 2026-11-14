using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Core.Types;
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
    IMemberFieldWriteCaseEmitter fieldWrites) : IRuntimeIntrinsicEmitter
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

    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        _validator.Validate(request);
        MemberExecutionPlan plan = request.Instruction.Target.MemberExecution;
        _roots.Emit(request.Instruction, code);
        var caseCount = request.Intrinsic switch
        {
            RuntimeIntrinsic.MemberExecuteMethod => _methods.EmitCases(request, code, plan),
            RuntimeIntrinsic.MemberReadField => _fieldReads.EmitCases(request, code, plan),
            _ => _fieldWrites.EmitCases(request, code, plan),
        };
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned(
                (uint)request.FunctionIndices.Resolve(plan.UnsupportedTarget!))));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(
                (uint)request.Local(0, CliValueKind.ManagedReference))));
        for (var index = 0; index < caseCount; index++)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        }
    }
}
