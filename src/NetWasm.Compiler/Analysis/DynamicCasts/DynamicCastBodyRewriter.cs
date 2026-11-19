// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.DynamicCasts;

// A body of a program that allocates a class implementing IDynamicInterfaceCastable, where an object may be cast to,
// stored as or called through an interface its class does not implement. Each interface cast (castclass, isinst,
// unbox.any) calls the helper that asks such an object, each reference array store the helper that admits it, and each
// interface call keeps its call for a receiver whose class implements the interface and otherwise asks the receiver
// for its implementation interface and calls that interface's method directly:
//
//   stloc args..., stloc receiver
//   ldloc receiver; brfalse static; ldloc receiver; isinst I; brtrue static
//   ldloc receiver; call Implementation<I>; stloc handle
//   (ldloc handle; ldtoken J; call Is; brfalse next; ldloc receiver; ldloc args...; call J.M; br end; next:)...
//   (ldloc handle; ldtoken K; call Is; brtrue missing)... for an implementation interface K without the method
//   ldloc receiver; ldloc handle; call InvalidImplementation<I>; throw
//   missing: call MissingImplementation; throw
//   static: ldloc receiver; ldloc args...; callvirt I.M
//   end:
internal sealed class DynamicCastBodyRewriter(
    ITypeOperandResolver typeOperands,
    ICalledMethodResolver calls,
    ITypeDefinitionResolver typeDefinitions,
    IDynamicImplementationInterfaceFinder implementations,
    IDispatchTargetResolver dispatchTargets,
    IDynamicCastHelperResolver helpers,
    CliTypeIdentity castable) : IDynamicCastBodyRewriter
{
    private readonly ITypeOperandResolver _typeOperands = typeOperands ?? throw new ArgumentNullException(nameof(typeOperands));
    private readonly ICalledMethodResolver _calls = calls ?? throw new ArgumentNullException(nameof(calls));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ?? throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IDynamicImplementationInterfaceFinder _implementations = implementations ??
        throw new ArgumentNullException(nameof(implementations));
    private readonly IDispatchTargetResolver _dispatchTargets = dispatchTargets ?? throw new ArgumentNullException(nameof(dispatchTargets));
    private readonly IDynamicCastHelperResolver _helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
    private readonly CliTypeIdentity _castable = castable ?? throw new ArgumentNullException(nameof(castable));

    public CilMethodBody Rewrite(ManagedMethodBody method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var body = method.Body;
        var replacements = new List<CilInstruction>[body.Instructions.Length];
        var locals = body.Locals.ToBuilder();
        var localTypes = body.LocalSignatureTypes.ToBuilder();
        var changed = false;
        var targets = BranchTargets(body);
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            if (DelegateOverInterfaceMethod(body, index, targets, locals, localTypes) is { } delegateCreation)
            {
                replacements[index] = delegateCreation;
                replacements[index + 1] = [];
                replacements[index + 2] = [];
                changed = true;
                index += 2;
                continue;
            }
            var replacement = instruction.Operation switch
            {
                CilOperation.CastClass or CilOperation.IsInstance or CilOperation.UnboxAny =>
                    Cast(instruction, method.Method),
                CilOperation.StoreArrayElementReference => [Call(_helpers.Resolve("StoreElement", []))],
                CilOperation.StoreArrayElement when _typeOperands.Resolve(instruction, method.Method) is
                { IsValueType: false, ContainsGenericParameters: false } => [Call(_helpers.Resolve("StoreElement", []))],
                CilOperation.CallVirtual when index == 0 || body.Instructions[index - 1].Operation != CilOperation.Constrained =>
                    InterfaceCall(instruction, locals, localTypes),
                _ => null,
            };
            changed |= replacement is not null;
            replacements[index] = replacement ?? [instruction];
        }
        // An interface call's splice holds the implementation handle and a candidate's type token, or the receiver and
        // the handle, above what was below the receiver: at most two more values than the call itself needed.
        return changed
            ? RenumberedInstructions.Splice(body, replacements) with
            {
                Locals = locals.ToImmutable(),
                LocalSignatureTypes = localTypes.ToImmutable(),
                MaxStack = checked(body.MaxStack + 2),
            }
            : body;
    }

    private List<CilInstruction>? Cast(CilInstruction instruction, MethodInstanceModel method)
    {
        var target = _typeOperands.Resolve(instruction, method);
        if (!IsInterface(target) || target.Equals(_castable)) return null;
        var helper = instruction.Operation == CilOperation.IsInstance ? "IsInstance" : "CastClass";
        return [Call(_helpers.Resolve(helper, [target]))];
    }

    private List<CilInstruction>? InterfaceCall(CilInstruction instruction, ImmutableArray<CliValueKind>.Builder locals,
        ImmutableArray<CliTypeIdentity>.Builder localTypes)
    {
        if (_calls.Resolve(instruction) is not { Definition.IsStatic: false } target || !IsInterface(target.DeclaringType) ||
            target.MethodArguments.Any(argument => argument.ContainsGenericParameters))
        {
            return null;
        }
        var interfaceType = target.DeclaringType;
        var implementation = _helpers.Resolve("Implementation", [interfaceType]);
        var receiver = AddLocal(locals, localTypes, interfaceType);
        var arguments = target.Signature.ParameterSignatureTypes.Select(type => AddLocal(locals, localTypes, type)).ToArray();
        var handle = AddLocal(locals, localTypes, implementation.Signature.ReturnSignatureType);
        var stores = new List<CilInstruction>();
        foreach (var argument in arguments.Reverse()) stores.Add(Op(CilOperation.StoreLocal, new CilOperand.Index(argument)));
        stores.Add(Op(CilOperation.StoreLocal, new CilOperand.Index(receiver)));
        // The end is the instruction after the call: the first of the next original instruction's list, which the
        // splice renumbers from the call's own next offset.
        return Dispatch(target, implementation, receiver, handle, stores,
            (code, method) =>
            {
                LoadArguments(code, receiver, arguments);
                code.Add(Call(method));
            },
            code =>
            {
                LoadArguments(code, receiver, arguments);
                code.Add(Op(instruction.Operation, instruction.Operand));
            },
            instruction.NextOffset);
    }

    // A delegate over an interface method (dup; ldvirtftn I.M; newobj D(object, native int)) binds, for a receiver whose
    // class does not implement I, to the implementation interface's method the receiver names, as .NET's ldvirtftn
    // resolves it; each candidate creates its delegate from that method directly:
    //
    //   stloc receiver
    //   ldloc receiver; brfalse static; ldloc receiver; isinst I; brtrue static
    //   ldloc receiver; call Implementation<I>; stloc handle
    //   (ldloc handle; ldtoken J; call Is; brfalse next; ldloc receiver; ldftn J.M; newobj D; br end; next:)...
    //   ldloc receiver; ldloc handle; call InvalidImplementation<I>; throw
    //   static: ldloc receiver; dup; ldvirtftn I.M; newobj D
    //   end:
    private List<CilInstruction>? DelegateOverInterfaceMethod(CilMethodBody body, int index, HashSet<int> targets,
        ImmutableArray<CliValueKind>.Builder locals, ImmutableArray<CliTypeIdentity>.Builder localTypes)
    {
        if (index + 2 >= body.Instructions.Length || body.Instructions[index].Operation != CilOperation.Duplicate ||
            body.Instructions[index + 1] is not { Operation: CilOperation.LoadVirtualFunction } load ||
            body.Instructions[index + 2] is not { Operation: CilOperation.NewObject } creation ||
            targets.Contains(load.Offset) || targets.Contains(creation.Offset) ||
            _calls.Resolve(load) is not { Definition.IsStatic: false } target || !IsInterface(target.DeclaringType) ||
            target.MethodArguments.Any(argument => argument.ContainsGenericParameters) ||
            _calls.Resolve(creation) is not { Signature.ParameterTypes.Length: 2 })
        {
            return null;
        }
        var interfaceType = target.DeclaringType;
        var implementation = _helpers.Resolve("Implementation", [interfaceType]);
        var receiver = AddLocal(locals, localTypes, interfaceType);
        var handle = AddLocal(locals, localTypes, implementation.Signature.ReturnSignatureType);
        return Dispatch(target, implementation, receiver, handle, [Op(CilOperation.StoreLocal, new CilOperand.Index(receiver))],
            (code, method) =>
            {
                code.Add(Op(CilOperation.LoadLocal, new CilOperand.Index(receiver)));
                code.Add(Op(CilOperation.LoadFunction, new CilOperand.MethodInstance(method)));
                code.Add(Op(CilOperation.NewObject, creation.Operand));
            },
            code =>
            {
                code.Add(Op(CilOperation.LoadLocal, new CilOperand.Index(receiver)));
                code.Add(Op(CilOperation.Duplicate, new CilOperand.None()));
                code.Add(Op(load.Operation, load.Operand));
                code.Add(Op(creation.Operation, creation.Operand));
            },
            creation.NextOffset);
    }

    // The dispatch both shapes share, after code has stored the receiver (in receiver) and what it needs: a null
    // receiver, or one whose class implements the interface, runs the original; otherwise the receiver's
    // implementation handle selects the candidate whose method runs, one without the method throws
    // MissingImplementation, and a handle naming none throws InvalidImplementation. Each candidate's code branches to
    // end.
    private List<CilInstruction> Dispatch(MethodInstanceModel target, MethodInstanceModel implementation, int receiver,
        int handle, List<CilInstruction> code, Action<List<CilInstruction>, MethodInstanceModel> candidateCode,
        Action<List<CilInstruction>> originalCode, int end)
    {
        var interfaceType = target.DeclaringType;
        var staticBranches = new List<int>();
        code.Add(Op(CilOperation.LoadLocal, new CilOperand.Index(receiver)));
        staticBranches.Add(code.Count);
        code.Add(Op(CilOperation.BranchIfFalse, new CilOperand.None()));
        code.Add(Op(CilOperation.LoadLocal, new CilOperand.Index(receiver)));
        code.Add(Op(CilOperation.IsInstance, new CilOperand.TypeIdentity(interfaceType)));
        staticBranches.Add(code.Count);
        code.Add(Op(CilOperation.BranchIfTrue, new CilOperand.None()));
        code.Add(Op(CilOperation.LoadLocal, new CilOperand.Index(receiver)));
        code.Add(Call(implementation));
        code.Add(Op(CilOperation.StoreLocal, new CilOperand.Index(handle)));
        var endBranches = new List<int>();
        var missingBranches = new List<int>();
        // An object may claim any interface, so a call through one no implementation interface derives from still asks
        // it, for the answer's validation to fail as .NET's does.
        foreach (var candidate in _implementations.Find(interfaceType))
        {
            var method = _dispatchTargets.Resolve(target, candidate)?.Method;
            code.Add(Op(CilOperation.LoadLocal, new CilOperand.Index(handle)));
            code.Add(Op(CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(candidate)));
            code.Add(Call(_helpers.Resolve("Is", [])));
            if (method is null)
            {
                // A valid implementation interface without the method.
                missingBranches.Add(code.Count);
                code.Add(Op(CilOperation.BranchIfTrue, new CilOperand.None()));
                continue;
            }
            var next = code.Count;
            code.Add(Op(CilOperation.BranchIfFalse, new CilOperand.None()));
            candidateCode(code, method);
            endBranches.Add(code.Count);
            code.Add(Op(CilOperation.Branch, new CilOperand.None()));
            code[next] = code[next] with { Operand = RenumberedInstructions.Local(code.Count) };
        }
        code.Add(Op(CilOperation.LoadLocal, new CilOperand.Index(receiver)));
        code.Add(Op(CilOperation.LoadLocal, new CilOperand.Index(handle)));
        code.Add(Call(_helpers.Resolve("InvalidImplementation", [interfaceType])));
        code.Add(Op(CilOperation.Throw, new CilOperand.None()));
        foreach (var branch in missingBranches) code[branch] = code[branch] with { Operand = RenumberedInstructions.Local(code.Count) };
        if (missingBranches.Count != 0)
        {
            code.Add(Call(_helpers.Resolve("MissingImplementation", [])));
            code.Add(Op(CilOperation.Throw, new CilOperand.None()));
        }
        foreach (var branch in staticBranches) code[branch] = code[branch] with { Operand = RenumberedInstructions.Local(code.Count) };
        originalCode(code);
        foreach (var branch in endBranches) code[branch] = code[branch] with { Operand = new CilOperand.BranchTarget(end) };
        return code;
    }

    // The offsets a branch names, which a pattern must not span (a switch is lowered to branches when its body is read).
    // No exception region begins inside one: a region begins with nothing or the exception on the stack and nothing
    // falls into it, while dup falls into ldvirtftn and ldvirtftn into newobj.
    private static HashSet<int> BranchTargets(CilMethodBody body) =>
        [.. body.Instructions.Select(instruction => instruction.Operand).OfType<CilOperand.BranchTarget>().Select(branch => branch.Offset)];

    private static void LoadArguments(List<CilInstruction> code, int receiver, int[] arguments)
    {
        code.Add(Op(CilOperation.LoadLocal, new CilOperand.Index(receiver)));
        foreach (var argument in arguments) code.Add(Op(CilOperation.LoadLocal, new CilOperand.Index(argument)));
    }

    private bool IsInterface(CliTypeIdentity type) =>
        !type.ContainsGenericParameters && !type.IsValueType &&
        type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation &&
        _typeDefinitions.ResolveTypeIdentity(type).IsInterface;

    private static int AddLocal(ImmutableArray<CliValueKind>.Builder locals, ImmutableArray<CliTypeIdentity>.Builder localTypes,
        CliTypeIdentity type)
    {
        locals.Add(type.StackKind);
        localTypes.Add(type);
        return localTypes.Count - 1;
    }

    private static CilInstruction Call(MethodInstanceModel method) => Op(CilOperation.Call, new CilOperand.MethodInstance(method));

    private static CilInstruction Op(CilOperation operation, CilOperand operand) => new(0, 1, operation, operand);
}
