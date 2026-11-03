using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// A vector's indexer reads through the vector's address, so a local whose
// element it reads, as Linq's Sum reads its accumulator's lanes, lives in a
// value frame and every operation on it in a loop stores and loads it. The
// indexer is the vector's GetElement, which takes the value: an indexer call
// on a local or an argument that one load of the index separates from its
// address reads the value instead, which keeps the vector in a v128 local.
// The JIT treats both as one intrinsic.
internal sealed class VectorElementReadRewriter(
    ICalledMethodResolver calledMethods,
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver instances,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    private readonly ConcurrentDictionary<(string Type, string Element), MethodInstanceModel> _targets = new();

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ImmutableArray<CilInstruction>.Builder? rewritten = null;
        var instructions = body.Instructions;
        var entered = Entered(body);
        for (var index = 2; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.Operation != CilOperation.Call ||
                calledMethods.Resolve(call) is not
                {
                    Definition: { Name: "get_Item", IsStatic: false },
                    DeclaringType: { Shape: CliTypeShape.GenericInstantiation, TypeArguments: [var element] } vector,
                } ||
                StaticClass(vector) is not { } owner ||
                instructions[index - 1].Operation is not
                    (CilOperation.LoadLocal or CilOperation.LoadArgument or CilOperation.LoadInt32) ||
                entered.Contains(instructions[index - 1].Offset) || entered.Contains(call.Offset) ||
                instructions[index - 2] is not { Operand: CilOperand.Index { Value: var slot } } address ||
                Value(address.Operation) is not { } load ||
                !Holds(body, address.Operation, slot, vector))
            {
                continue;
            }
            var getElement = Resolve(owner, element);
            if (getElement.Definition.Key == body.Method.Key) continue;
            rewritten ??= instructions.ToBuilder();
            rewritten[index - 2] = address with { Operation = load };
            rewritten[index] = call with { Operand = new CilOperand.MethodInstance(getElement) };
        }
        return rewritten is null ? body : body with { Instructions = rewritten.ToImmutable() };
    }

    private static CilOperation? Value(CilOperation address) => address switch
    {
        CilOperation.LoadLocalAddress => CilOperation.LoadLocal,
        CilOperation.LoadArgumentAddress => CilOperation.LoadArgument,
        _ => null,
    };

    // Whether the local or argument is of the vector's own type, as verifiable
    // code's address of it is. An instance method's arguments start with this,
    // which is no vector.
    private static bool Holds(CilMethodBody body, CilOperation address, int slot, CliTypeIdentity vector)
    {
        if (address == CilOperation.LoadLocalAddress)
        {
            return body.LocalSignatureTypes[slot].Equals(vector);
        }
        var parameters = (body.MethodInstance?.Signature ?? body.Method.Signature).ParameterSignatureTypes;
        CliTypeIdentity?[] arguments = body.Method.IsStatic ? [.. parameters] : [null, .. parameters];
        return Equals(arguments[slot], vector);
    }

    // The offsets a branch or a protected region enters at, where another
    // path could arrive with another address.
    private static ImmutableHashSet<int> Entered(CilMethodBody body) =>
    [
        .. body.Instructions
            .Select(static instruction => instruction.Operand)
            .OfType<CilOperand.BranchTarget>()
            .Select(static target => target.Offset),
        .. body.ExceptionRegions.SelectMany(region =>
            region.FilterOffset is { } filter
                ? new[] { region.TryOffset, region.HandlerOffset, filter }
                : [region.TryOffset, region.HandlerOffset]),
    ];

    // The static class whose GetElement reads a Vector128 or Vector instantiation,
    // whose element type is its generic definition.
    private static string? StaticClass(CliTypeIdentity vector) => vector.ElementType!.FullName switch
    {
        "System.Runtime.Intrinsics.Vector128`1" => "System.Runtime.Intrinsics.Vector128",
        "System.Numerics.Vector`1" => "System.Numerics.Vector",
        _ => null,
    };

    private MethodInstanceModel Resolve(string type, CliTypeIdentity element) =>
        _targets.GetOrAdd((type, element.CanonicalName), _ =>
        {
            // Each class has one generic GetElement; Vector's others read a Vector2, Vector3 or Vector4.
            var method = types.FindType(type).Methods.Select(methods.GetMethod)
                .Single(method => method is { Name: "GetElement", GenericArity: 1 });
            return instances.ResolveMethodInstance(method.Key.Assembly, method.Key.MetadataToken, symbols.Format(method), 0,
                new CliGenericContext([], [element]));
        });
}
