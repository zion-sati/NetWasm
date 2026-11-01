using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Allocations;

// Inlining leaves values nothing reads: a receiver spilled for a callee that
// never touches it, or the object allocated only to make that call. A store to
// a local that is never read becomes a pop, a pure push that is popped
// disappears, and so does an allocation whose constructor only initializes the
// new object, when no finalizer could observe it and no constructor in its chain
// runs a static constructor first (ECMA-335 I.8.9.5). .NET's JIT removes such
// allocations once escape analysis proves them unused.
internal sealed class UnusedAllocationEliminator(
    IMethodBodyReader methodBodies,
    ICalledMethodResolver calledMethods,
    IFinalizerResolver finalizers,
    IMethodInitializationTriggerClassifier initializationTriggers,
    bool enabled) : IMethodSpecializer
{
    // Constructor chains are short; deeper ones stay allocations.
    private const int MaximumConstructorDepth = 8;
    private readonly IMethodBodyReader _methodBodies = methodBodies ?? throw new ArgumentNullException(nameof(methodBodies));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly IFinalizerResolver _finalizers = finalizers ?? throw new ArgumentNullException(nameof(finalizers));
    private readonly IMethodInitializationTriggerClassifier _initializationTriggers = initializationTriggers ??
        throw new ArgumentNullException(nameof(initializationTriggers));
    private readonly ConcurrentDictionary<string, bool> _pure = new(StringComparer.Ordinal);
    private readonly object _finalizerLock = new();

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!enabled)
        {
            return body;
        }
        var instructions = body.Instructions.ToArray();
        var entries = InstructionEntries.Of(body);
        var changed = false;
        // Each pass removes at least one instruction, so the passes end.
        for (var progress = true; progress;)
        {
            progress = false;
            var read = instructions
                .Where(static instruction => instruction.Operation is CilOperation.LoadLocal or CilOperation.LoadLocalAddress)
                .Select(static instruction => ((CilOperand.Index)instruction.Operand).Value)
                .ToHashSet();
            for (var index = 0; index < instructions.Length; index++)
            {
                if (instructions[index] is { Operation: CilOperation.StoreLocal, Operand: CilOperand.Index stored } &&
                    !read.Contains(stored.Value))
                {
                    instructions[index] = instructions[index] with { Operation = CilOperation.Pop, Operand = new CilOperand.None() };
                    progress = true;
                }
            }
            for (var index = 0; index < instructions.Length; index++)
            {
                if (instructions[index].Operation == CilOperation.Nop ||
                    Next(instructions, entries, index) is not { } next ||
                    instructions[next].Operation != CilOperation.Pop ||
                    Drops(instructions[index]) is not { } pops)
                {
                    continue;
                }
                // The pushed value is dropped; keep popping what it consumed.
                instructions[index] = pops > 0 ? Pop(instructions[index]) : Nop(instructions[index]);
                instructions[next] = pops > 1 ? instructions[next] : Nop(instructions[next]);
                progress = true;
            }
            changed |= progress;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    // How many values an instruction whose result is unused still has to
    // drop, or null when it has an effect of its own.
    private int? Drops(CilInstruction instruction) => instruction.Operation switch
    {
        CilOperation.LoadLocal or CilOperation.LoadArgument or CilOperation.LoadInt32 or CilOperation.LoadInt64 or
            CilOperation.LoadFloat32 or CilOperation.LoadFloat64 or CilOperation.LoadNull or CilOperation.Duplicate => 0,
        CilOperation.NewObject when _calledMethods.Resolve(instruction) is { DeclaringType.IsValueType: false } constructor &&
            constructor.Signature.ParameterTypes.Length <= 2 && !Finalizable(constructor.DeclaringType) &&
            IsPure(constructor, 0) => constructor.Signature.ParameterTypes.Length,
        _ => null,
    };

    private bool Finalizable(CliTypeIdentity type)
    {
        lock (_finalizerLock)
        {
            return _finalizers.Resolve(type) is not null;
        }
    }

    private static readonly CliTypeIdentity StringType =
        CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, isValueType: false);

    // A constructor that only stores constants and arguments into the new
    // object, after a base constructor that does the same, of a type whose
    // construction does not run its static constructor. A string's
    // constructors are placeholders: the compiler builds the string and
    // checks the arguments, so a discarded one still runs.
    private bool IsPure(MethodInstanceModel constructor, int depth) =>
        depth < MaximumConstructorDepth && constructor.Definition.HasManagedBody &&
        !constructor.DeclaringType.Equals(StringType) &&
        !_initializationTriggers.Classify(constructor.Definition) &&
        _pure.GetOrAdd(constructor.CanonicalName, _ => StoresOnlyIntoItself(constructor, depth));

    private bool StoresOnlyIntoItself(MethodInstanceModel constructor, int depth)
    {
        var body = _methodBodies.ReadMethodBody(constructor);
        if (!body.ExceptionRegions.IsEmpty)
        {
            return false;
        }
        // True for the new object, false for any other value.
        var stack = new Stack<bool>();
        foreach (var instruction in body.Instructions)
        {
            switch (instruction)
            {
                case { Operation: CilOperation.Nop }:
                    break;
                case { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index argument }:
                    stack.Push(argument.Value == 0);
                    break;
                case
                {
                    Operation: CilOperation.LoadInt32 or CilOperation.LoadInt64 or CilOperation.LoadFloat32 or
                    CilOperation.LoadFloat64 or CilOperation.LoadNull
                }:
                    stack.Push(false);
                    break;
                case { Operation: CilOperation.StoreField } when stack.Count >= 2 && !stack.Pop() && stack.Pop():
                    break;
                case { Operation: CilOperation.Call } when
                    _calledMethods.Resolve(instruction) is { Definition: { IsStatic: false, Name: ".ctor" } } chained &&
                    stack.Count == chained.Signature.ParameterTypes.Length + 1 &&
                    stack.Take(chained.Signature.ParameterTypes.Length).All(static value => !value) &&
                    stack.Last() && IsPure(chained, depth + 1):
                    stack.Clear();
                    break;
                case { Operation: CilOperation.Return } when stack.Count == 0:
                    return true;
                default:
                    return false;
            }
        }
        return false;
    }

    private static int? Next(CilInstruction[] instructions, HashSet<int> entries, int index)
    {
        for (var next = index + 1; next < instructions.Length; next++)
        {
            if (entries.Contains(instructions[next].Offset))
            {
                return null;
            }
            if (instructions[next].Operation != CilOperation.Nop)
            {
                return next;
            }
        }
        return null;
    }

    private static CilInstruction Pop(CilInstruction instruction) =>
        instruction with { Operation = CilOperation.Pop, Operand = new CilOperand.None() };

    private static CilInstruction Nop(CilInstruction instruction) =>
        instruction with { Operation = CilOperation.Nop, Operand = new CilOperand.None() };
}
