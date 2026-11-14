using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal sealed class ConstantArgumentProgramReducer(
    ICallArgumentConstantCollector arguments,
    IMethodSpecializer rewrites) : IReachableProgramReducer
{
    private readonly ICallArgumentConstantCollector _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    private readonly IMethodSpecializer _rewrites = rewrites ?? throw new ArgumentNullException(nameof(rewrites));

    public ImmutableDictionary<string, CilMethodBody> Reduce(ReachableProgram program, ReachabilityRoots roots)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(roots);
        var bodies = program.Methods.Values.Concat(program.ConstructedMethods.Values).DistinctBy(body => body.Method.CanonicalName).ToArray();
        var boundaryKeys = roots.Methods.Concat(roots.InvokedMethods).Concat(program.Exports.Select(export => export.Method)).ToHashSet();
        if (program.EntryPoint is not null) boundaryKeys.Add(program.EntryPoint.Key);
        var boundaries = program.CallableMethods.Keys.Concat(program.NativeCallbacks.Keys)
            .Concat(program.MethodDescriptors.Values.Select(method => method.CanonicalName))
            .Concat(program.DispatchCallSites.Values.SelectMany(site => site.Targets.Select(target => target.Method.CanonicalName)))
            .ToHashSet(StringComparer.Ordinal);
        var inputs = new Dictionary<string, ScalarConstant?[]>(StringComparer.Ordinal);
        foreach (var body in bodies)
        {
            foreach (var call in _arguments.Collect(body))
            {
                if (!inputs.TryGetValue(call.Method.CanonicalName, out var known))
                {
                    inputs.Add(call.Method.CanonicalName, call.Arguments.ToArray());
                    continue;
                }
                if (known.Length != call.Arguments.Length) throw new InvalidOperationException("Incoming calls disagree on their target signature.");
                for (var index = 0; index < known.Length; index++)
                {
                    if (known[index] != call.Arguments[index]) known[index] = null;
                }
            }
        }

        var result = ImmutableDictionary.CreateBuilder<string, CilMethodBody>(StringComparer.Ordinal);
        foreach (var method in bodies)
        {
            if (boundaryKeys.Contains(method.Method.Definition.Key) ||
                boundaries.Contains(method.Method.CanonicalName) || !inputs.TryGetValue(method.Method.CanonicalName, out var known)) continue;
            // An instance method's argument 0 is its receiver; the collected constants are its declared parameters.
            var receiver = method.Method.Definition.IsStatic ? 0 : 1;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction is { Operation: CilOperation.LoadArgumentAddress or CilOperation.StoreArgument, Operand: CilOperand.Index index } &&
                    (uint)(index.Value - receiver) < (uint)known.Length) known[index.Value - receiver] = null;
            }

            var changed = false;
            var instructions = method.Body.Instructions.Select(instruction =>
            {
                if (instruction is not { Operation: CilOperation.LoadArgument, Operand: CilOperand.Index index } ||
                    (uint)(index.Value - receiver) >= (uint)known.Length || known[index.Value - receiver] is not { } constant) return instruction;
                var kind = method.Method.Signature.ParameterTypes[index.Value - receiver];
                if (constant is ScalarConstant.Int32 integer && kind == CliValueKind.I4)
                {
                    changed = true;
                    return instruction with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(integer.Value) };
                }
                if (constant is ScalarConstant.Null && kind == CliValueKind.ManagedReference)
                {
                    changed = true;
                    return instruction with { Operation = CilOperation.LoadNull, Operand = new CilOperand.None() };
                }
                return instruction;
            }).ToImmutableArray();
            if (changed)
            {
                result.Add(method.Method.CanonicalName, _rewrites.Rewrite(WithInstructions(method.Body, instructions)));
            }
        }
        return result.ToImmutable();
    }

    // The constants replace argument loads one for one, and every call passes them, so the facts the body's array and
    // integer proofs record still hold: they move to the new instructions. A specializer that then rewrites the body
    // further leaves them describing these instructions, and emission ignores them.
    private static CilMethodBody WithInstructions(CilMethodBody body, ImmutableArray<CilInstruction> instructions) =>
        body with
        {
            Instructions = instructions,
            ArrayBoundsProof = body.ArrayBoundsProof is { } bounds &&
                bounds.Describes(body.Instructions, body.Locals, body.LocalSignatureTypes)
                ? bounds with { Instructions = instructions }
                : body.ArrayBoundsProof,
            IntegerCheckProof = body.IntegerCheckProof is { } integers && integers.Instructions == body.Instructions
                ? integers with { Instructions = instructions }
                : body.IntegerCheckProof,
        };
}
