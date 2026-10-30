using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

// Vector128<T> and Vector<T> are both 16 bytes in NetWasm. The operations here
// lower to Wasm SIMD for every element type the vectors support, so they need
// no managed body; everything else keeps its upstream software implementation.
internal static class SimdVectorIntrinsicClassifier
{
    private const string Vector128 = "System.Runtime.Intrinsics.Vector128";
    private const string Vector128OfT = "System.Runtime.Intrinsics.Vector128`1";
    private const string Vector = "System.Numerics.Vector";
    private const string VectorOfT = "System.Numerics.Vector`1";

    public static RuntimeIntrinsic? Classify(string declaringType, MethodDefinitionModel method)
    {
        if (!method.IsStatic)
        {
            return declaringType is Vector128OfT or VectorOfT && method.Name == "get_Item" &&
                method.Signature.ParameterSignatureTypes is [{ CanonicalName: "primitive:i4" }]
                    ? RuntimeIntrinsic.SimdGetElement
                    : null;
        }

        if (declaringType is Vector128OfT or VectorOfT)
        {
            return method.Name switch
            {
                "op_Addition" when Binary(method) => RuntimeIntrinsic.SimdAdd,
                "op_Subtraction" when Binary(method) => RuntimeIntrinsic.SimdSubtract,
                "op_Multiply" when Binary(method) || ByScalar(method) || ScalarBy(method) => RuntimeIntrinsic.SimdMultiply,
                "op_Division" when Binary(method) || ByScalar(method) => RuntimeIntrinsic.SimdDivide,
                "op_BitwiseAnd" when Binary(method) => RuntimeIntrinsic.SimdBitwiseAnd,
                "op_BitwiseOr" when Binary(method) => RuntimeIntrinsic.SimdBitwiseOr,
                "op_ExclusiveOr" when Binary(method) => RuntimeIntrinsic.SimdExclusiveOr,
                "op_OnesComplement" when Unary(method) => RuntimeIntrinsic.SimdOnesComplement,
                "op_UnaryNegation" when Unary(method) => RuntimeIntrinsic.SimdNegate,
                "op_Equality" when Predicate(method) => RuntimeIntrinsic.SimdEqualsAll,
                "op_Inequality" when Predicate(method) => RuntimeIntrinsic.SimdNotEqualsAll,
                "op_LeftShift" when Shift(method) => RuntimeIntrinsic.SimdShiftLeft,
                "op_RightShift" when Shift(method) => RuntimeIntrinsic.SimdShiftRightArithmetic,
                "op_UnsignedRightShift" when Shift(method) => RuntimeIntrinsic.SimdShiftRightLogical,
                _ => null,
            };
        }

        if (declaringType is not (Vector128 or Vector))
        {
            return null;
        }

        return method.Name switch
        {
            "Add" when Binary(method) => RuntimeIntrinsic.SimdAdd,
            "Subtract" when Binary(method) => RuntimeIntrinsic.SimdSubtract,
            "Multiply" when Binary(method) || ByScalar(method) || ScalarBy(method) => RuntimeIntrinsic.SimdMultiply,
            "MultiplyAddEstimate" when Ternary(method) => RuntimeIntrinsic.SimdMultiplyAddEstimate,
            "Divide" when Binary(method) || ByScalar(method) => RuntimeIntrinsic.SimdDivide,
            "Sum" when method.GenericArity == 1 &&
                method.Signature.ParameterSignatureTypes is [var vector] && IsVector(vector) &&
                method.Signature.ReturnSignatureType.Shape == CliTypeShape.GenericMethodParameter =>
                RuntimeIntrinsic.SimdSum,
            "Sqrt" or "SquareRoot" when Unary(method) => RuntimeIntrinsic.SimdSquareRoot,
            "Floor" when Unary(method) => RuntimeIntrinsic.SimdFloor,
            "Ceiling" when Unary(method) => RuntimeIntrinsic.SimdCeiling,
            "Truncate" when Unary(method) => RuntimeIntrinsic.SimdTruncate,
            "Round" when Unary(method) => RuntimeIntrinsic.SimdRound,
            "WidenLower" when Unary(method) => RuntimeIntrinsic.SimdWidenLower,
            "WidenUpper" when Unary(method) => RuntimeIntrinsic.SimdWidenUpper,
            "Narrow" when Binary(method) => RuntimeIntrinsic.SimdNarrow,
            // ConvertToInt32, ConvertToSingle, ConvertToDouble and the rest, with
            // their Native variants.
            var name when name.StartsWith("ConvertTo", StringComparison.Ordinal) && Unary(method) =>
                RuntimeIntrinsic.SimdConvert,
            "BitwiseAnd" when Binary(method) => RuntimeIntrinsic.SimdBitwiseAnd,
            "BitwiseOr" when Binary(method) => RuntimeIntrinsic.SimdBitwiseOr,
            "Xor" when Binary(method) => RuntimeIntrinsic.SimdExclusiveOr,
            "AndNot" when Binary(method) => RuntimeIntrinsic.SimdAndNot,
            "OnesComplement" when Unary(method) => RuntimeIntrinsic.SimdOnesComplement,
            "Negate" when Unary(method) => RuntimeIntrinsic.SimdNegate,
            "Abs" when Unary(method) => RuntimeIntrinsic.SimdAbsolute,
            "Min" when Binary(method) => RuntimeIntrinsic.SimdMinimum,
            "Max" when Binary(method) => RuntimeIntrinsic.SimdMaximum,
            "Equals" when Binary(method) => RuntimeIntrinsic.SimdEquals,
            "GreaterThan" when Binary(method) => RuntimeIntrinsic.SimdGreaterThan,
            "GreaterThanOrEqual" when Binary(method) => RuntimeIntrinsic.SimdGreaterThanOrEqual,
            "LessThan" when Binary(method) => RuntimeIntrinsic.SimdLessThan,
            "LessThanOrEqual" when Binary(method) => RuntimeIntrinsic.SimdLessThanOrEqual,
            "ConditionalSelect" when IsVector(method.Signature.ReturnSignatureType) &&
                method.Signature.ParameterSignatureTypes is [var condition, var left, var right] &&
                IsVector(condition) && IsVector(left) && IsVector(right) =>
                RuntimeIntrinsic.SimdConditionalSelect,
            "EqualsAll" when Predicate(method) => RuntimeIntrinsic.SimdEqualsAll,
            "LessThanAll" or "LessThanOrEqualAll" or "GreaterThanAll" or "GreaterThanOrEqualAll" when Predicate(method) =>
                RuntimeIntrinsic.SimdCompareAll,
            "LessThanAny" or "LessThanOrEqualAny" or "GreaterThanAny" or "GreaterThanOrEqualAny" when Predicate(method) =>
                RuntimeIntrinsic.SimdCompareAny,
            "EqualsAny" when Predicate(method) => RuntimeIntrinsic.SimdEqualsAny,
            "ExtractMostSignificantBits" when
                method.Signature.ParameterSignatureTypes is [var vector] && IsVector(vector) &&
                method.Signature.ReturnSignatureType.CanonicalName == "primitive:u4" =>
                RuntimeIntrinsic.SimdExtractMostSignificantBits,
            "Create" when method.GenericArity == 1 &&
                IsVector(method.Signature.ReturnSignatureType) &&
                method.Signature.ParameterSignatureTypes is [{ Shape: CliTypeShape.GenericMethodParameter }] =>
                RuntimeIntrinsic.SimdCreateBroadcast,
            "Create" when method.GenericArity == 0 => CreateFromElements(method),
            "LoadUnsafe" when Load(method, CliTypeShape.ManagedByReference) => RuntimeIntrinsic.SimdLoad,
            "Load" when Load(method, CliTypeShape.UnmanagedPointer) => RuntimeIntrinsic.SimdLoad,
            "StoreUnsafe" when Store(method, CliTypeShape.ManagedByReference) => RuntimeIntrinsic.SimdStore,
            "Store" when Store(method, CliTypeShape.UnmanagedPointer) => RuntimeIntrinsic.SimdStore,
            "GetElement" when method.GenericArity == 1 &&
                method.Signature.ParameterSignatureTypes is [var vector, { CanonicalName: "primitive:i4" }] &&
                IsVector(vector) &&
                method.Signature.ReturnSignatureType.Shape == CliTypeShape.GenericMethodParameter =>
                RuntimeIntrinsic.SimdGetElement,
            // As<TFrom, TTo>, AsByte, AsVectorInt64, AsVector128 and the rest:
            // the same 16 bytes viewed with another element type.
            var name when name.StartsWith("As", StringComparison.Ordinal) && Unary(method) =>
                RuntimeIntrinsic.SimdReinterpret,
            "ToScalar" when method.GenericArity == 1 &&
                method.Signature.ParameterSignatureTypes is [var vector] && IsVector(vector) &&
                method.Signature.ReturnSignatureType.Shape == CliTypeShape.GenericMethodParameter =>
                RuntimeIntrinsic.SimdToScalar,
            _ => null,
        };
    }

    // The non-generic overloads: one element broadcasts, one per lane fills the
    // lanes in order.
    private static RuntimeIntrinsic? CreateFromElements(MethodDefinitionModel method)
    {
        var result = method.Signature.ReturnSignatureType;
        if (!IsVector(result) || result.TypeArguments is not [var element] ||
            method.Signature.ParameterSignatureTypes.IsEmpty ||
            !method.Signature.ParameterSignatureTypes.All(parameter => parameter.Equals(element)))
        {
            return null;
        }

        int? lanes = element.CanonicalName switch
        {
            "primitive:i1" or "primitive:u1" => 16,
            "primitive:i2" or "primitive:u2" => 8,
            "primitive:i4" or "primitive:u4" or "primitive:f4" => 4,
            "primitive:i8" or "primitive:u8" or "primitive:f8" => 2,
            "primitive:nativeint" or "primitive:nativeuint" => null,
            _ => 0,
        };
        var count = method.Signature.ParameterSignatureTypes.Length;
        return count == 1 && lanes != 0 ? RuntimeIntrinsic.SimdCreateBroadcast
            : count == lanes ? RuntimeIntrinsic.SimdCreateElements
            : null;
    }

    // Load(T*), LoadUnsafe(ref readonly T) and LoadUnsafe(ref readonly T, nuint).
    private static bool Load(MethodDefinitionModel method, CliTypeShape address) =>
        method.GenericArity == 1 &&
        IsVector(method.Signature.ReturnSignatureType) &&
        method.Signature.ParameterSignatureTypes switch
        {
            [var source] => source.Shape == address,
            [var source, var offset] => address == CliTypeShape.ManagedByReference &&
                source.Shape == address && offset.CanonicalName == "primitive:nativeuint",
            _ => false,
        };

    // Store(T*) and StoreUnsafe(ref T) with an optional nuint element offset.
    private static bool Store(MethodDefinitionModel method, CliTypeShape address) =>
        method.GenericArity == 1 &&
        method.Signature.ReturnType == CliValueKind.Void &&
        method.Signature.ParameterSignatureTypes switch
        {
            [var source, var destination] => IsVector(source) && destination.Shape == address,
            [var source, var destination, var offset] => address == CliTypeShape.ManagedByReference &&
                IsVector(source) && destination.Shape == address &&
                offset.CanonicalName == "primitive:nativeuint",
            _ => false,
        };

    private static bool Unary(MethodDefinitionModel method) =>
        IsVector(method.Signature.ReturnSignatureType) &&
        method.Signature.ParameterSignatureTypes is [var value] && IsVector(value);

    private static bool Binary(MethodDefinitionModel method) =>
        IsVector(method.Signature.ReturnSignatureType) &&
        method.Signature.ParameterSignatureTypes is [var left, var right] &&
        IsVector(left) && IsVector(right);

    private static bool Ternary(MethodDefinitionModel method) =>
        IsVector(method.Signature.ReturnSignatureType) &&
        method.Signature.ParameterSignatureTypes is [var left, var right, var addend] &&
        IsVector(left) && IsVector(right) && IsVector(addend);

    // A vector divided by a scalar of its element type.
    private static bool ByScalar(MethodDefinitionModel method) =>
        IsVector(method.Signature.ReturnSignatureType) &&
        method.Signature.ParameterSignatureTypes is [var left, var right] &&
        IsVector(left) && !IsVector(right);

    // A scalar of the element type times a vector.
    private static bool ScalarBy(MethodDefinitionModel method) =>
        IsVector(method.Signature.ReturnSignatureType) &&
        method.Signature.ParameterSignatureTypes is [var left, var right] &&
        !IsVector(left) && IsVector(right);

    private static bool Predicate(MethodDefinitionModel method) =>
        method.Signature.ReturnSignatureType.CanonicalName == "primitive:bool" &&
        method.Signature.ParameterSignatureTypes is [var left, var right] &&
        IsVector(left) && IsVector(right);

    private static bool Shift(MethodDefinitionModel method) =>
        IsVector(method.Signature.ReturnSignatureType) &&
        method.Signature.ParameterSignatureTypes is [var value, var count] &&
        IsVector(value) && count.CanonicalName == "primitive:i4";

    internal static bool IsVector(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.GenericInstantiation &&
        type.ElementType?.FullName is Vector128OfT or VectorOfT;
}
