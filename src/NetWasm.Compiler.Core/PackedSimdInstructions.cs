using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.Core;

public enum PackedSimdShape
{
    // Vector, scalar and pointer operands in parameter order, then the instruction.
    Simple,
    // As Simple, with a default memory argument.
    Memory,
    // The lane index operand need not be a constant, so these read and write
    // the lane through the vector's storage.
    ExtractLane,
    ReplaceLane,
    LoadLane,
    StoreLane,
}

// The 64-bit-lane form a native-width overload takes on wasm64. Wasm has no
// unsigned 64-bit lane comparisons, so those compare with the sign bit flipped.
public readonly record struct PackedSimdWideInstruction(
    string Name,
    uint Opcode,
    int LaneBytes,
    bool FlipsSign);

public readonly record struct PackedSimdInstruction(
    string Name,
    uint Opcode,
    PackedSimdShape Shape,
    int LaneBytes,
    bool SignedLane,
    PackedSimdWideInstruction Wide);

// Upstream's System.Runtime.Intrinsics.Wasm.PackedSimd overloads, each one
// Wasm SIMD instruction, keyed by name and parameter kinds.
public static partial class PackedSimdInstructions
{
    public const string DeclaringType = "System.Runtime.Intrinsics.Wasm.PackedSimd";

    public static bool TryGet(MethodDefinitionModel method, out PackedSimdInstruction instruction)
    {
        instruction = default;
        return method.IsStatic && method.GenericArity == 0 &&
            TryKey(method.Name, method.Signature.ParameterSignatureTypes, out var key) &&
            Table.TryGetValue(key, out instruction);
    }

    // Whether the overload's elements or scalars are nint or nuint.
    public static bool IsNativeWidth(MethodSignatureModel signature) =>
        signature.ParameterSignatureTypes.Append(signature.ReturnSignatureType)
            .Select(Element)
            .Any(name => name is "primitive:nativeint" or "primitive:nativeuint");

    private static bool TryKey(string name, IEnumerable<CliTypeIdentity> parameters, out string key)
    {
        var tokens = new List<string>();
        foreach (var parameter in parameters)
        {
            var prefix = parameter.Shape switch
            {
                CliTypeShape.GenericInstantiation when
                    parameter.ElementType?.FullName == "System.Runtime.Intrinsics.Vector128`1" => "v:",
                CliTypeShape.UnmanagedPointer => "p:",
                CliTypeShape.Primitive => "s:",
                _ => null,
            };
            var element = Element(parameter);
            if (prefix is null || !element.StartsWith("primitive:", System.StringComparison.Ordinal))
            {
                key = "";
                return false;
            }
            tokens.Add(prefix + element["primitive:".Length..]);
        }
        key = $"{name}({string.Join(',', tokens)})";
        return true;
    }

    private static string Element(CliTypeIdentity type) => type.Shape switch
    {
        CliTypeShape.GenericInstantiation when type.TypeArguments is [var element] => element.CanonicalName,
        CliTypeShape.UnmanagedPointer => type.ElementType?.CanonicalName ?? "",
        _ => type.CanonicalName,
    };
}
