// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

// Which P/Invokes compile to a marshalling stub, and the native signature its stub calls. A position desktop .NET's
// runtime marshalling converts, a class, an array or a HandleRef, by value or by reference, becomes a native-sized
// integer, as its IL stub passes it; a bool or char, by value or by reference, becomes the native scalar it marshals as
// (NativeScalarMarshalling); every other position passes unchanged. A [MarshalAs] its position cannot take also makes a
// stub, whose builder reports it where a program reaches the method. The stub's builder, which knows the types, accepts
// the handles and blittable arrays NetWasm marshals and rejects the rest.
internal static class NativeMarshallingStubSignature
{
    private const string HandleRef = "System.Runtime.InteropServices.HandleRef";

    // The inner native method's token: the stub's own with the high bit set, which no metadata token has.
    public static int NativeMethodToken(int stubToken) => stubToken | unchecked((int)0x80000000);

    public static MethodSignatureModel? Lower(MethodSignatureModel signature) => Lower(signature, import: null);

    // With the import, as an assembly that keeps runtime marshalling declares it, its bool and char positions and its
    // [MarshalAs] types take part. With its structs' native layouts, a struct that converts (NativeStructLayout) passes
    // as the Wasm C ABI passes its native copy: by value as its single scalar or as the copy's address, by reference as
    // the copy's address, and as a result as its single scalar or through a leading pointer to the native result,
    // which the function then returns nothing for.
    public static MethodSignatureModel? Lower(MethodSignatureModel signature, NativeImportDeclaration? import,
        Func<CliTypeIdentity, NativeStructLayout?>? structs = null)
    {
        var lowered = false;
        var attributes = import?.Attributes ?? default;
        var parameters = signature.ParameterSignatureTypes
            .Select((type, index) => Struct(type, structs, ref lowered) ?? Lower(type,
                import is null ? null : import.MarshalAs[index], attributes, import is not null, ref lowered))
            .ToImmutableArray();
        var resultType = signature.ReturnSignatureType;
        if (structs is not null && Unmodified(resultType) is var result && IsStruct(result) && structs(result) is { Converts: true } layout)
        {
            lowered = true;
            return layout.Scalar is { } scalar
                ? new MethodSignatureModel(scalar, parameters)
                : new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void), parameters.Insert(0, NativeInt));
        }
        var loweredResult = Lower(resultType, import?.MarshalAs.Result, attributes, import is not null, ref lowered);
        return lowered ? new MethodSignatureModel(loweredResult, parameters) : null;
    }

    private static readonly CliTypeIdentity NativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);

    // A struct parameter that converts: its single scalar, or the address of its native copy.
    private static CliTypeIdentity? Struct(CliTypeIdentity type, Func<CliTypeIdentity, NativeStructLayout?>? structs,
        ref bool lowered)
    {
        if (structs is null)
            return null;
        var byReference = type.Shape == CliTypeShape.ManagedByReference;
        var value = Unmodified(byReference ? type.ElementType! : type);
        if (!IsStruct(value) || structs(value) is not { Converts: true } layout)
            return null;
        lowered = true;
        return !byReference && layout.Scalar is { } scalar ? scalar : NativeInt;
    }

    // A struct a stub may lay out natively: a named value type other than HandleRef, which passes as its handle.
    internal static bool IsStruct(CliTypeIdentity type) =>
        type.IsValueType && type.Shape == CliTypeShape.Named && type.FullName != HandleRef;

    private static CliTypeIdentity Lower(CliTypeIdentity type, UnmanagedType? marshalAs, MethodImportAttributes attributes,
        bool scalars, ref bool lowered)
    {
        var nativeInt = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
        if (Converts(type))
        {
            lowered = true;
            return nativeInt;
        }
        if (type.Shape == CliTypeShape.ManagedByReference && Converts(type.ElementType!))
        {
            lowered = true;
            return CliTypeIdentity.ManagedByReference(nativeInt);
        }
        if (!scalars)
        {
            return type;
        }
        var byReference = type.Shape == CliTypeShape.ManagedByReference;
        var value = Unmodified(byReference ? type.ElementType! : type);
        if (NativeScalarMarshalling.Converts(value))
        {
            lowered = true;
            // A [MarshalAs] the position cannot take keeps its type; the stub's builder reports it.
            return NativeScalarMarshalling.Form(value, marshalAs, attributes) is { } form
                ? byReference ? CliTypeIdentity.ManagedByReference(form.Native) : form.Native
                : type;
        }
        if (value.Shape == CliTypeShape.Primitive && NativeScalarMarshalling.Mismatch(value, marshalAs) is not null)
        {
            lowered = true;
        }
        return type;
    }

    private static CliTypeIdentity Unmodified(CliTypeIdentity type)
    {
        while (type.Shape == CliTypeShape.Modified)
            type = type.ElementType!;
        return type;
    }

    private static bool Converts(CliTypeIdentity type)
    {
        while (type.Shape == CliTypeShape.Modified)
            type = type.ElementType!;
        return type.Shape switch
        {
            CliTypeShape.SzArray or CliTypeShape.Array => true,
            CliTypeShape.Named or CliTypeShape.GenericInstantiation or CliTypeShape.Primitive =>
                type.StackKind == CliValueKind.ManagedReference || type.FullName == HandleRef,
            _ => false,
        };
    }
}
