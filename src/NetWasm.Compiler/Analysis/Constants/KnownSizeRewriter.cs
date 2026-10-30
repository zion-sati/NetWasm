using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// `sizeof(T)` and `Unsafe.SizeOf<T>()` of a type whose size every NetWasm
// target shares become the constant, as the JIT folds them, so upstream's
// `if (sizeof(T) == 2)` and `Vector<byte>.Count / sizeof(T)` branches fold with
// the specialized T. The sizes are the primitives' and the vector types' own;
// pointer-sized and laid-out types keep their instruction.
internal sealed class KnownSizeRewriter(ICalledMethodResolver calledMethods) : IMethodRewriteRule
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ImmutableArray<CilInstruction>.Builder? rewritten = null;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            var type = instruction switch
            {
                { Operation: CilOperation.SizeOf, Operand: CilOperand.TypeIdentity { Value: var sized } } => sized,
                { Operation: CilOperation.Call } when _calledMethods.Resolve(instruction) is
                {
                    Definition: { Name: "SizeOf", IsStatic: true },
                    DeclaringType.FullName: "System.Runtime.CompilerServices.Unsafe",
                    MethodArguments: [var argument],
                } => argument,
                _ => null,
            };
            if (type is null || Size(type) is not { } size) continue;
            rewritten ??= body.Instructions.ToBuilder();
            rewritten[index] = instruction with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(size) };
        }
        return rewritten is null ? body : body with { Instructions = rewritten.ToImmutable() };
    }

    internal static int? Size(CliTypeIdentity type) => type.CanonicalName switch
    {
        "primitive:bool" or "primitive:i1" or "primitive:u1" => 1,
        "primitive:char" or "primitive:i2" or "primitive:u2" => 2,
        "primitive:i4" or "primitive:u4" or "primitive:f4" => 4,
        "primitive:i8" or "primitive:u8" or "primitive:f8" => 8,
        _ when type is { Shape: CliTypeShape.GenericInstantiation, ElementType.FullName: { } vector } => vector switch
        {
            "System.Runtime.Intrinsics.Vector64`1" => 8,
            "System.Runtime.Intrinsics.Vector128`1" or "System.Numerics.Vector`1" => 16,
            "System.Runtime.Intrinsics.Vector256`1" => 32,
            "System.Runtime.Intrinsics.Vector512`1" => 64,
            _ => null,
        },
        _ => null,
    };
}
