using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class ConstantCallEvaluator(
    IMethodBodyReader methodBodies,
    ICalledMethodResolver calledMethods,
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IRuntimeIntrinsicRegistry intrinsics,
    IConstantOperandReader operands,
    IConstantComparisonEvaluator comparisons,
    IMethodSpecializer specializer) : IConstantCallEvaluator
{
    private readonly IMethodBodyReader _methodBodies = methodBodies ?? throw new ArgumentNullException(nameof(methodBodies));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ?? throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IMethodRepository _methods = methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IRuntimeIntrinsicRegistry _intrinsics = intrinsics ?? throw new ArgumentNullException(nameof(intrinsics));
    private readonly IConstantOperandReader _operands = operands ?? throw new ArgumentNullException(nameof(operands));
    private readonly IConstantComparisonEvaluator _comparisons = comparisons ?? throw new ArgumentNullException(nameof(comparisons));
    private readonly IMethodSpecializer _specializer = specializer ?? throw new ArgumentNullException(nameof(specializer));
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CilMethodBody> _bodies = new(StringComparer.Ordinal);

    public ScalarConstant? Evaluate(ConstantCallEvaluation request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        if (request.Arguments.IsDefault || request.Arguments.Length != ArgumentCount(request.Method))
        {
            throw new ArgumentException("Constant arguments must match the method signature.", nameof(request));
        }
        if (request.Arguments.All(argument => argument is not null) &&
            _comparisons.Evaluate(request.Method, [.. request.Arguments.Select(argument => argument!)]) is { } compared)
        {
            return compared;
        }
        var active = new HashSet<string>(StringComparer.Ordinal);
        var first = CreateFrame(request, active);
        if (first is null) return null;
        var frames = new Stack<EvaluationFrame>();
        frames.Push(first);
        while (true)
        {
            var frame = frames.Peek();
            // A repeated instruction is not a finite proof; decline the fold.
            if (!frame.Visited.Add(frame.Offset) || !frame.Instructions.TryGetValue(frame.Offset, out var instruction)) return null;
            frame.Offset = instruction.NextOffset;
            var stack = frame.Values;
            var locals = frame.Locals;
            if (ReadOperand(frame, instruction) is var (operand, next))
            {
                stack.Add(operand);
                frame.Offset = next;
                continue;
            }
            switch (instruction)
            {
                case { Operation: CilOperation.Nop }:
                    break;
                case { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index argument }:
                    if ((uint)argument.Value >= (uint)frame.Request.Arguments.Length || frame.Request.Arguments[argument.Value] is not { } known)
                    {
                        return null;
                    }
                    stack.Add(known);
                    break;
                case { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index local }:
                    if (!TryPop(stack, out var stored)) return null;
                    locals[local.Value] = stored;
                    break;
                case { Operation: CilOperation.LoadLocal, Operand: CilOperand.Index local }:
                    if (!locals.TryGetValue(local.Value, out var loaded)) return null;
                    stack.Add(loaded);
                    break;
                case { Operation: CilOperation.Duplicate }:
                    if (stack.Count == 0) return null;
                    stack.Add(stack[^1]);
                    break;
                case { Operation: CilOperation.Pop }:
                    if (!TryPop(stack, out _)) return null;
                    break;
                case { Operation: CilOperation.CompareEqual }:
                    if (!TryPop(stack, out var right) || !TryPop(stack, out var left) || AreIdentical(left, right) is not { } same) return null;
                    stack.Add(new ScalarConstant.Int32(same ? 1 : 0));
                    break;
                case { Operation: CilOperation.Branch, Operand: CilOperand.BranchTarget branch }:
                    frame.Offset = branch.Offset;
                    break;
                case { Operation: CilOperation.BranchIfTrue or CilOperation.BranchIfFalse, Operand: CilOperand.BranchTarget branch }:
                    if (!TryPop(stack, out var condition) || IsTrue(condition) is not { } truth) return null;
                    if (truth == (instruction.Operation == CilOperation.BranchIfTrue)) frame.Offset = branch.Offset;
                    break;
                case { Operation: CilOperation.BranchIfEqual or CilOperation.BranchIfNotEqual, Operand: CilOperand.BranchTarget branch }:
                    if (!TryPop(stack, out var comparedRight) || !TryPop(stack, out var comparedLeft) ||
                        AreIdentical(comparedLeft, comparedRight) is not { } equal) return null;
                    if (equal == (instruction.Operation == CilOperation.BranchIfEqual)) frame.Offset = branch.Offset;
                    break;
                case { Operation: CilOperation.Call or CilOperation.CallVirtual }:
                    var target = _calledMethods.Resolve(instruction);
                    if (target is null || stack.Count < ArgumentCount(target)) return null;
                    var count = ArgumentCount(target);
                    var arguments = stack.GetRange(stack.Count - count, count);
                    stack.RemoveRange(stack.Count - count, count);
                    if (_comparisons.Evaluate(target, arguments) is { } comparison)
                    {
                        stack.Add(comparison);
                        break;
                    }
                    if (instruction.Operation != CilOperation.Call) return null;
                    var child = CreateFrame(new(target, [.. arguments]), active);
                    if (child is null) return null;
                    frames.Push(child);
                    break;
                case { Operation: CilOperation.Return }:
                    if (stack.Count != 1 || !Matches(stack[0], frame.Request.Method.Signature.ReturnSignatureType)) return null;
                    var returned = stack[0];
                    frames.Pop();
                    active.Remove(frame.Request.Method.CanonicalName);
                    if (frames.Count == 0) return returned;
                    frames.Peek().Values.Add(returned);
                    break;
                default:
                    // No field/array access, allocation, address-taking, arithmetic
                    // with possible exceptions, virtual calls or other effects.
                    return null;
            }
        }

    }

    private static int ArgumentCount(MethodInstanceModel method) =>
        method.Signature.ParameterTypes.Length + (method.Definition.IsStatic ? 0 : 1);

    // Reads a producer sequence that starts at the instruction along its fall-through path.
    private (ScalarConstant Value, int NextOffset)? ReadOperand(EvaluationFrame frame, CilInstruction first)
    {
        var producers = new List<CilInstruction>(_operands.MaximumLength) { first };
        while (true)
        {
            if (_operands.Read(producers) is { } value) return (value, producers[^1].NextOffset);
            if (producers.Count == _operands.MaximumLength || !frame.Instructions.TryGetValue(producers[^1].NextOffset, out var next)) return null;
            producers.Add(next);
        }
    }

    private EvaluationFrame? CreateFrame(ConstantCallEvaluation request, HashSet<string> active)
    {
        var method = request.Method;
        if (!method.Definition.IsStatic || method.Definition.IsVirtual ||
            method.Definition.IsNoInlining || !method.Definition.HasManagedBody ||
            method.Definition.JSImport is not null || method.Definition.WitImport is not null || method.Definition.NativeImport is not null ||
            _intrinsics.TryGetIntrinsic(method.Definition.Key, out _) ||
            method.Signature.ReturnType is not (CliValueKind.I4 or CliValueKind.ManagedReference) ||
            active.Contains(method.CanonicalName))
        {
            return null;
        }
        var owner = _typeDefinitions.ResolveTypeIdentity(method.DeclaringType);
        if (!owner.IsBeforeFieldInit && owner.Methods.Any(key => _methods.GetMethod(key).Name == ".cctor"))
        {
            return null;
        }
        for (var index = 0; index < request.Arguments.Length; index++)
        {
            if (request.Arguments[index] is { } argument && !Matches(argument, method.Signature.ParameterSignatureTypes[index])) return null;
        }
        // The callee as its closed instantiation specializes it, so its typeof
        // comparisons and sizes are already constants, as when it is inlined.
        var body = _bodies.GetOrAdd(method.CanonicalName, _ => _specializer.Rewrite(_methodBodies.ReadMethodBody(method)));
        if (!body.ExceptionRegions.IsEmpty || body.Instructions.IsEmpty) return null;
        active.Add(method.CanonicalName);
        return new(request, body);
    }

    private sealed class EvaluationFrame(ConstantCallEvaluation request, CilMethodBody body)
    {
        public ConstantCallEvaluation Request { get; } = request;
        public Dictionary<int, CilInstruction> Instructions { get; } = body.Instructions.ToDictionary(instruction => instruction.Offset);
        public HashSet<int> Visited { get; } = [];
        public Dictionary<int, ScalarConstant> Locals { get; } = [];
        public List<ScalarConstant> Values { get; } = [];
        public int Offset { get; set; } = body.Instructions[0].Offset;
    }

    private static bool Matches(ScalarConstant value, CliTypeIdentity type) => value switch
    {
        ScalarConstant.Int32 => type.StackKind == CliValueKind.I4,
        ScalarConstant.Null or ScalarConstant.String => type.StackKind == CliValueKind.ManagedReference,
        ScalarConstant.Bytes => ReadOnlySpanDataResolver.TryGetReadOnlySpanElement(type, out var element) &&
            element.CanonicalName == "primitive:u1",
        _ => false,
    };

    private static bool? IsTrue(ScalarConstant value) => value switch
    {
        ScalarConstant.Int32 integer => integer.Value != 0,
        ScalarConstant.Null => false,
        ScalarConstant.String => true,
        _ => null,
    };

    // ceq/beq semantics: integer equality or reference identity.
    private static bool? AreIdentical(ScalarConstant left, ScalarConstant right) => (left, right) switch
    {
        (ScalarConstant.Int32 first, ScalarConstant.Int32 second) => first.Value == second.Value,
        (ScalarConstant.Null, ScalarConstant.Null) => true,
        (ScalarConstant.Null, ScalarConstant.String) or (ScalarConstant.String, ScalarConstant.Null) => false,
        // Different contents are different objects; equal literals depend on interning.
        (ScalarConstant.String first, ScalarConstant.String second) when first.Value != second.Value => false,
        _ => null,
    };

    private static bool TryPop(List<ScalarConstant> stack, out ScalarConstant value)
    {
        if (stack.Count == 0)
        {
            value = null!;
            return false;
        }
        value = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return true;
    }
}
