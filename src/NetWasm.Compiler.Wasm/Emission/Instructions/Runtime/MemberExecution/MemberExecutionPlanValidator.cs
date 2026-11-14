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

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime.MemberExecution;

// The signature each member intrinsic has, and a plan whose facts agree with the cases emitted for it.
internal sealed class MemberExecutionPlanValidator(INullableTypeResolver nullableTypes) : IMemberExecutionPlanValidator
{
    private readonly INullableTypeResolver _nullableTypes = nullableTypes ??
        throw new ArgumentNullException(nameof(nullableTypes));

    public void Validate(RuntimeIntrinsicEmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSignature(request);
        MemberExecutionPlan plan = request.Instruction.Target.MemberExecution;
        if (plan.UnsupportedTarget is null)
        {
            throw RuntimeContract("bounded member execution has no compiler plan");
        }
        ValidatePlan(
            request.Intrinsic == RuntimeIntrinsic.MemberExecuteMethod,
            plan);
        if (request.Intrinsic == RuntimeIntrinsic.MemberExecuteMethod &&
            plan.Methods.Values.Any(method =>
                _nullableTypes.Resolve(method.Descriptor.Signature.ReturnSignatureType) is not null) &&
            !request.Instruction.Context.ValueLayout.MemberResultOffsets.ContainsKey(
                request.Instruction.Instruction.Offset))
        {
            throw RuntimeContract("nullable member results require planned return storage");
        }
    }

    private static void ValidateSignature(RuntimeIntrinsicEmissionRequest request)
    {
        ReadOnlySpan<CliValueKind> expectedParameters = request.Intrinsic switch
        {
            RuntimeIntrinsic.MemberExecuteMethod =>
            [
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference,
            ],
            RuntimeIntrinsic.MemberReadField =>
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            RuntimeIntrinsic.MemberWriteField =>
            [CliValueKind.ManagedReference, CliValueKind.ManagedReference, CliValueKind.ManagedReference],
            _ => throw RuntimeContract(
                $"runtime intrinsic '{request.Intrinsic}' is not bounded member execution"),
        };
        if (!request.Method.Signature.ParameterTypes.AsSpan().SequenceEqual(
                expectedParameters) ||
            request.Method.Signature.ReturnType != CliValueKind.ManagedReference)
        {
            throw RuntimeContract(
                "bounded member execution contradicts its runtime signature");
        }
    }

    private void ValidatePlan(
        bool executeMethod,
        MemberExecutionPlan plan)
    {
        if (executeMethod)
        {
            foreach (var pair in plan.Methods)
            {
                var execution = pair.Value;
                var method = execution.Descriptor;
                if (!StringComparer.Ordinal.Equals(pair.Key, method.CanonicalName) ||
                    execution.RequiresDispatch !=
                        (method.Definition.IsVirtual && !method.DeclaringType.IsValueType) ||
                    method.Definition.IsStatic && execution.RequiresDispatch ||
                    !execution.RequiresDispatch && !execution.Targets.IsEmpty)
                {
                    throw RuntimeContract(
                        "bounded method execution contains contradictory dispatch facts");
                }

                if (method.Signature.ReturnSignatureType.StackKind != CliValueKind.Void)
                {
                    ValidateResultType(method.Signature.ReturnSignatureType);
                }
                foreach (var parameter in method.Signature.ParameterSignatureTypes)
                {
                    ValidateValueKind(parameter.StackKind);
                }
                foreach (var target in execution.Targets)
                {
                    if (target.Method.Definition.IsStatic ||
                        !DispatchResultMatches(
                            method.Signature.ReturnSignatureType,
                            target.Method.Signature.ReturnSignatureType) ||
                        !target.Method.Signature.ParameterSignatureTypes.SequenceEqual(
                            method.Signature.ParameterSignatureTypes))
                    {
                        throw RuntimeContract(
                            "bounded method execution contains a contradictory dispatch target");
                    }
                }
            }
            return;
        }
        foreach (var pair in plan.Fields)
        {
            if (!StringComparer.Ordinal.Equals(
                    pair.Key,
                    pair.Value.CanonicalName) ||
                pair.Value.Definition.IsStatic && pair.Value.Definition.IsLiteral)
            {
                throw RuntimeContract(
                    "bounded field execution contains contradictory facts");
            }
            ValidateResultType(pair.Value.FieldType);
        }
    }

    private void ValidateResultType(CliTypeIdentity type)
    {
        if (type.StackKind == CliValueKind.ValueType &&
            !type.ContainsGenericParameters &&
            _nullableTypes.Resolve(type) is
            {
                StackKind:
                CliValueKind.I4 or CliValueKind.I8 or CliValueKind.F4 or CliValueKind.F8
            })
        {
            return;
        }
        ValidateValueKind(type.StackKind);
    }

    private static void ValidateValueKind(CliValueKind kind)
    {
        if (kind is not (
                CliValueKind.ManagedReference or
                CliValueKind.I4 or CliValueKind.I8 or
                CliValueKind.F4 or CliValueKind.F8 or
                CliValueKind.ValueType))
        {
            throw RuntimeContract(
                $"bounded member execution cannot preserve '{kind}'");
        }
    }

    private static bool DispatchResultMatches(
        CliTypeIdentity descriptor,
        CliTypeIdentity target) =>
        descriptor.StackKind == CliValueKind.ManagedReference
            ? target.StackKind == CliValueKind.ManagedReference
            : descriptor.Equals(target);

    private static CompilerException RuntimeContract(string message) => new(
        new CompilerDiagnostic(DiagnosticCode.RuntimeContract, message));
}
