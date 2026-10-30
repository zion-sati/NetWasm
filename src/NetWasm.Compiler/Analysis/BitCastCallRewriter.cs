using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// Unsafe.BitCast<TFrom, TTo> reads its argument through the address of a
// copy, which gives each caller a value frame and a store and load per call.
// Between values Wasm holds in registers the JIT reinterprets them in place,
// and so does NetWasm: a double and a 64-bit integer, or a float and a 32-bit
// integer, become BitConverter's reinterpretation, two integers of one width
// or one type need nothing, and two Vector128 or two Vector instantiations
// become the vector's own As. Other types, laid out in memory, keep the call,
// and so does the body of the method a cast would become.
internal sealed class BitCastCallRewriter(
    ICalledMethodResolver calledMethods,
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver instances,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    private readonly ConcurrentDictionary<(string Type, string Name, string Arguments), MethodInstanceModel> _targets = new();

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ImmutableArray<CilInstruction>.Builder? rewritten = null;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var instruction = body.Instructions[index];
            if (instruction.Operation != CilOperation.Call ||
                calledMethods.Resolve(instruction) is not
                {
                    Definition: { Name: "BitCast", IsStatic: true },
                    DeclaringType.FullName: "System.Runtime.CompilerServices.Unsafe",
                    MethodArguments: [var from, var to],
                })
            {
                continue;
            }
            if (Target(from, to) is not { } cast) continue;
            var (type, name, arguments) = cast;
            CilOperand replacement = new CilOperand.None();
            if (type is not null)
            {
                var method = Resolve(type, name!, arguments);
                if (method.Definition.Key == body.Method.Key) continue;
                replacement = new CilOperand.MethodInstance(method);
            }
            rewritten ??= body.Instructions.ToBuilder();
            rewritten[index] = instruction with
            {
                Operation = type is null ? CilOperation.Nop : CilOperation.Call,
                Operand = replacement,
            };
        }
        return rewritten is null ? body : body with { Instructions = rewritten.ToImmutable() };
    }

    // The method a cast becomes, by declaring type, name and type arguments;
    // no type and name when the stack holds both types alike; null when the
    // cast keeps its call.
    private static (string? Type, string? Name, ImmutableArray<CliTypeIdentity> Arguments)? Target(
        CliTypeIdentity from, CliTypeIdentity to) =>
        (from.CanonicalName, to.CanonicalName) switch
        {
            ("primitive:f8", "primitive:i8" or "primitive:u8") => ("System.BitConverter", "DoubleToInt64Bits", []),
            ("primitive:i8" or "primitive:u8", "primitive:f8") => ("System.BitConverter", "Int64BitsToDouble", []),
            ("primitive:f4", "primitive:i4" or "primitive:u4") => ("System.BitConverter", "SingleToInt32Bits", []),
            ("primitive:i4" or "primitive:u4", "primitive:f4") => ("System.BitConverter", "Int32BitsToSingle", []),
            ("primitive:i4" or "primitive:u4", "primitive:i4" or "primitive:u4") or
                ("primitive:i8" or "primitive:u8", "primitive:i8" or "primitive:u8") or
                ("primitive:f4", "primitive:f4") or ("primitive:f8", "primitive:f8") => (null, null, []),
            _ => Vector(from) is { } vector && vector == Vector(to)
                ? (vector, "As", [from.TypeArguments[0], to.TypeArguments[0]])
                : null,
        };

    // The static class whose As reinterprets a Vector128 or Vector instantiation.
    private static string? Vector(CliTypeIdentity type) =>
        type is { Shape: CliTypeShape.GenericInstantiation, ElementType.FullName: { } definition }
            ? definition switch
            {
                "System.Runtime.Intrinsics.Vector128`1" => "System.Runtime.Intrinsics.Vector128",
                "System.Numerics.Vector`1" => "System.Numerics.Vector",
                _ => null,
            }
            : null;

    private MethodInstanceModel Resolve(string type, string name, ImmutableArray<CliTypeIdentity> arguments) =>
        _targets.GetOrAdd((type, name, string.Join(",", arguments.Select(static argument => argument.CanonicalName))), _ =>
        {
            // Each is the only method of its name: BitConverter's conversions and the vectors' As.
            var method = types.FindType(type).Methods.Select(methods.GetMethod).Single(method => method.Name == name);
            return instances.ResolveMethodInstance(method.Key.Assembly, method.Key.MetadataToken, symbols.Format(method), 0,
                new CliGenericContext([], arguments));
        });
}
