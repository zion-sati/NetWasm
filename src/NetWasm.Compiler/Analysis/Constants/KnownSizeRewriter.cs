using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

// `sizeof(T)`, `Unsafe.SizeOf<T>()` and `IntPtr.Size` become the constant, as the JIT folds them,
// so upstream's `if (sizeof(T) == 2)`, `Vector<byte>.Count / sizeof(T)` and `if (nint.Size == 8)`
// branches fold before reachability and the untaken side's callees and data are not kept. The
// sizes are the primitives' and the vector types' own, and the compiled target's native integer
// size for native integers and pointers; laid-out types keep their instruction.
internal sealed class KnownSizeRewriter(ICalledMethodResolver calledMethods, int nativeIntegerSize) : IMethodRewriteRule
{
    private readonly ICalledMethodResolver _calledMethods = calledMethods ?? throw new ArgumentNullException(nameof(calledMethods));
    private readonly int _nativeIntegerSize = nativeIntegerSize is 4 or 8
        ? nativeIntegerSize
        : throw new ArgumentOutOfRangeException(nameof(nativeIntegerSize));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ImmutableArray<CilInstruction>.Builder? rewritten = null;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            var known = instruction switch
            {
                { Operation: CilOperation.SizeOf, Operand: CilOperand.TypeIdentity { Value: var sized } } => TargetSize(sized),
                { Operation: CilOperation.Call } => _calledMethods.Resolve(instruction) switch
                {
                    {
                        Definition: { Name: "SizeOf", IsStatic: true },
                        DeclaringType.FullName: "System.Runtime.CompilerServices.Unsafe",
                        MethodArguments: [var argument],
                    } => TargetSize(argument),
                    { Definition: { Name: "get_Size", IsStatic: true }, DeclaringType: var declaring }
                        when IsNativeInteger(declaring) => _nativeIntegerSize,
                    _ => null,
                },
                _ => null,
            };
            if (known is not { } size) continue;
            rewritten ??= body.Instructions.ToBuilder();
            rewritten[index] = instruction with { Operation = CilOperation.LoadInt32, Operand = new CilOperand.ConstantI4(size) };
        }
        return rewritten is null ? body : body with { Instructions = rewritten.ToImmutable() };
    }

    private int? TargetSize(CliTypeIdentity type) =>
        IsNativeInteger(type) || type.Shape is CliTypeShape.UnmanagedPointer or CliTypeShape.FunctionPointer
            ? _nativeIntegerSize
            : Size(type);

    private static bool IsNativeInteger(CliTypeIdentity type) =>
        type.CanonicalName is "primitive:nativeint" or "primitive:nativeuint";

    // The sizes every target shares.
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
