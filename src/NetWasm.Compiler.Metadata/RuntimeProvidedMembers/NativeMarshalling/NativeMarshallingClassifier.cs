// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// Chooses how a marshalling stub passes each parameter and result. NetWasm marshals what desktop .NET passes without
// converting it: SafeHandle and CriticalHandle values, results and by-reference parameters, HandleRef values and arrays
// of blittable elements, which it pins; and it converts bool and char values, by value, by reference and as results,
// string arguments, and structs holding a bool or char through their native copy, as desktop .NET does on Unix
// (NativeScalarMarshalling, NativeStringMarshalling, NativeStructLayouts). Anything else
// desktop .NET's runtime marshalling converts (string results, strings by reference, StringBuilder, delegates, classes
// with layout, arrays that need their elements converted) is outside NetWasm's native profile and fails the build where
// a program reaches it.
internal sealed class NativeMarshallingClassifier(
    ITypeDefinitionResolver definitions,
    IBaseTypeIdentityResolver baseTypes,
    IMethodRepository methods,
    IFieldRepository fields,
    NativeMarshallingMembers members,
    INativeStructCopierFactory copiers) : INativeMarshallingClassifier
{
    // The native signature tells the struct positions apart: one the stub's declaration lowered to its native copy
    // (NativeMarshallingStubSignature) passes as that copy, and a native function with a parameter more than the stub
    // returns its struct result through it.
    public NativeStubStreams Classify(MethodInstanceModel stub, NativeImportDeclaration import, MethodSignatureModel native)
    {
        ArgumentNullException.ThrowIfNull(stub);
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(native);
        var name = stub.CanonicalName;
        var parameters = stub.Signature.ParameterSignatureTypes;
        // A variadic call's native method takes its fixed parameters and then the buffer for the rest, and none of its
        // positions is a struct's native copy (NativeMarshallingStubSignature), so each keeps its own type.
        var variadic = stub.Definition.NativeMarshallingStub?.Variadic is not null;
        var leading = variadic ? 0 : native.ParameterSignatureTypes.Length - parameters.Length;
        return new(
            [.. parameters.Select((type, index) =>
                StructArgument(index, type, variadic ? type : native.ParameterSignatureTypes[leading + index], import, name) ??
                Argument(index, type, import.ParameterFlows[index], import.MarshalAs[index], import.Attributes, name))],
            StructResult(stub.Signature.ReturnSignatureType, native, leading == 1, import, name) ??
            Result(stub.Signature.ReturnSignatureType, import.MarshalAs.Result, import.Attributes, name));
    }

    // A struct parameter the declaration lowered to its native copy, by value or by reference.
    private NativeArgumentStreams? StructArgument(int argument, CliTypeIdentity type, CliTypeIdentity native,
        NativeImportDeclaration import, string method)
    {
        type = Unmodified(type);
        var byReference = type.Shape == CliTypeShape.ManagedByReference;
        var value = Unmodified(byReference ? type.ElementType! : type);
        if (!NativeMarshallingStubSignature.IsStruct(value) || native.Equals(type))
            return null;
        var flow = import.ParameterFlows[argument];
        return StructArgumentMarshaller.Plan(argument, Copier(value, import.Attributes, method), native, byReference,
            flow == NativeParameterFlow.None || (flow & NativeParameterFlow.In) != 0,
            flow == NativeParameterFlow.None || (flow & NativeParameterFlow.Out) != 0);
    }

    // A struct result the declaration lowered: the native function returns its single scalar, or nothing when it
    // writes the result through its leading pointer.
    private NativeResultStreams? StructResult(CliTypeIdentity type, MethodSignatureModel native, bool throughPointer,
        NativeImportDeclaration import, string method)
    {
        type = Unmodified(type);
        if (!NativeMarshallingStubSignature.IsStruct(type) || !throughPointer && native.ReturnSignatureType.Equals(type))
            return null;
        return StructResultMarshaller.Plan(type, Copier(type, import.Attributes, method),
            throughPointer ? null : native.ReturnSignatureType);
    }

    private INativeStructCopier Copier(CliTypeIdentity type, MethodImportAttributes attributes, string method) =>
        copiers.Create(type, Conversions(attributes)) ??
        throw Unsupported(method, "A struct passed to native code must lay out natively: no fields of reference types, " +
            "fixed arrays or strings by [MarshalAs], or auto layout.");

    private NativeArgumentStreams Argument(int argument, CliTypeIdentity type, NativeParameterFlow flow,
        UnmanagedType? marshalAs, MethodImportAttributes attributes, string method)
    {
        type = Unmodified(type);
        if (type.Shape == CliTypeShape.ManagedByReference && Unmodified(type.ElementType!) is var scalar &&
            NativeScalarMarshalling.Converts(scalar))
        {
            // By reference without [In] or [Out] is both, as on desktop .NET.
            return ByReferenceScalarArgumentMarshaller.Plan(argument, scalar, Scalar(scalar, marshalAs, attributes, method),
                flow == NativeParameterFlow.None || (flow & NativeParameterFlow.In) != 0,
                flow == NativeParameterFlow.None || (flow & NativeParameterFlow.Out) != 0, Conversions(attributes));
        }
        if (NativeScalarMarshalling.Converts(type))
        {
            return ScalarArgumentMarshaller.Plan(argument, Scalar(type, marshalAs, attributes, method), Conversions(attributes));
        }
        if (NativeStringMarshalling.IsString(type))
        {
            return StringArgumentMarshaller.Plan(argument, NativeStringMarshalling.Form(marshalAs, attributes) ??
                throw Unsupported(method, NativeStringMarshalling.Mismatch), members);
        }
        if (type.Shape == CliTypeShape.Primitive && NativeScalarMarshalling.Mismatch(type, marshalAs) is { } mismatch)
        {
            throw Unsupported(method, mismatch);
        }
        if (type.Shape == CliTypeShape.ManagedByReference && Unmodified(type.ElementType!) is var element &&
            !element.IsValueType)
        {
            var kind = Handle(element) ?? throw Unsupported(method,
                "A by-reference native parameter of a reference type must be a SafeHandle or CriticalHandle.");
            // By reference without [In] or [Out] is both, as on desktop .NET.
            var isIn = flow == NativeParameterFlow.None || (flow & NativeParameterFlow.In) != 0;
            var isOut = flow == NativeParameterFlow.None || (flow & NativeParameterFlow.Out) != 0;
            return ByReferenceHandleArgumentMarshaller.Plan(argument, element, kind, isIn, isOut,
                isOut ? Constructor(element, kind, "out", method) : null, members);
        }
        if (type.Shape == CliTypeShape.SzArray)
        {
            return IsBlittable(type.ElementType!, [])
                ? PinnedArrayArgumentMarshaller.Plan(argument, members)
                : throw Unsupported(method,
                    "A native array parameter must have blittable elements, which it passes pinned; bool, char and " +
                    "elements holding references need conversion.");
        }
        if (type.Shape == CliTypeShape.Array)
        {
            throw Unsupported(method, "A native array parameter must be a single-dimensional, zero-based array.");
        }
        if (type.FullName == NativeMarshallingMembers.HandleRefName)
        {
            return HandleRefArgumentMarshaller.Plan(argument, members);
        }
        if (type.StackKind == CliValueKind.ManagedReference)
        {
            return Handle(type) switch
            {
                HandleKind.Safe => SafeHandleArgumentMarshaller.Plan(argument, members),
                HandleKind.Critical => CriticalHandleArgumentMarshaller.Plan(argument, members),
                _ => throw Unsupported(method,
                    "A native parameter of a reference type must be a string, a SafeHandle, a CriticalHandle or an array " +
                    "of blittable elements; delegates and other classes need runtime marshalling."),
            };
        }
        return PassThroughArgumentMarshaller.Plan(argument);
    }

    private NativeResultStreams Result(CliTypeIdentity type, UnmanagedType? marshalAs, MethodImportAttributes attributes,
        string method)
    {
        type = Unmodified(type);
        if (NativeScalarMarshalling.Converts(type))
        {
            return ConvertingResultMarshaller.Plan(type, Scalar(type, marshalAs, attributes, method), Conversions(attributes));
        }
        if (NativeStringMarshalling.IsString(type))
        {
            return StringResultMarshaller.Plan(NativeStringMarshalling.Form(marshalAs, attributes) ??
                throw Unsupported(method, NativeStringMarshalling.Mismatch), members);
        }
        if (type.Shape == CliTypeShape.Primitive && NativeScalarMarshalling.Mismatch(type, marshalAs) is { } mismatch)
        {
            throw Unsupported(method, mismatch);
        }
        if (type.StackKind != CliValueKind.ManagedReference)
        {
            return type.FullName == NativeMarshallingMembers.HandleRefName
                ? throw Unsupported(method, "A HandleRef cannot be a native result.")
                : ValueResultMarshaller.Plan(type);
        }
        var kind = Handle(type) ?? throw Unsupported(method,
            "A native result of a reference type must be a string, a SafeHandle or a CriticalHandle.");
        return HandleResultMarshaller.Plan(type, Constructor(type, kind, "result", method), members.HandleFields.For(kind));
    }

    // A bool or char position's native form, or the reason desktop .NET rejects its [MarshalAs]: a bool or char has a
    // form for exactly the [MarshalAs] it takes.
    private static NativeScalarForm Scalar(CliTypeIdentity type, UnmanagedType? marshalAs, MethodImportAttributes attributes,
        string method) =>
        NativeScalarMarshalling.Form(type, marshalAs, attributes) ??
        throw Unsupported(method, NativeScalarMarshalling.Mismatch(type, marshalAs)!);

    // The conversions of the import's bool and char values, with its best-fit and unmappable-character settings.
    private NativeScalarConversions Conversions(MethodImportAttributes attributes) => new(members,
        BestFit: (attributes & MethodImportAttributes.BestFitMappingMask) != MethodImportAttributes.BestFitMappingDisable,
        ThrowOnUnmappableChar: (attributes & MethodImportAttributes.ThrowOnUnmappableCharMask) ==
            MethodImportAttributes.ThrowOnUnmappableCharEnable);

    // The SafeHandle or CriticalHandle family a class belongs to, if either.
    private HandleKind? Handle(CliTypeIdentity type)
    {
        if (type.Shape is not (CliTypeShape.Named or CliTypeShape.GenericInstantiation) || type.IsValueType)
        {
            return null;
        }
        for (CliTypeIdentity? current = type; current is not null; current = baseTypes.GetBaseTypeIdentity(current))
        {
            switch (current.FullName)
            {
                case NativeMarshallingMembers.SafeHandleName:
                    return HandleKind.Safe;
                case NativeMarshallingMembers.CriticalHandleName:
                    return HandleKind.Critical;
            }
        }
        return null;
    }

    // The parameterless constructor that makes a handle for a result or an out parameter, whatever its accessibility,
    // as desktop .NET calls it. Desktop .NET fails such a call when the handle type is abstract or has no such
    // constructor; NetWasm fails the build.
    private MethodInstanceModel Constructor(CliTypeIdentity type, HandleKind kind, string position, string method)
    {
        var definition = definitions.ResolveTypeIdentity(type);
        var family = kind == HandleKind.Safe ? "SafeHandle" : "CriticalHandle";
        if (definition.IsAbstract)
        {
            throw Unsupported(method, $"A {family} {position} must not be abstract: the stub makes the handle.");
        }
        var constructor = definition.Methods.Select(methods.GetMethod).SingleOrDefault(candidate =>
            candidate.Name == ".ctor" && !candidate.IsStatic && candidate.Signature.ParameterTypes.IsEmpty) ??
            throw Unsupported(method, $"A {family} {position} must have a parameterless constructor: the stub makes the handle.");
        return new(constructor, type, [], constructor.Signature);
    }

    // Desktop .NET's blittable types: the integers, float and double, pointers, enums, and structs with sequential or
    // explicit layout whose fields are all blittable. bool and char take conversion under runtime marshalling.
    private bool IsBlittable(CliTypeIdentity type, HashSet<CliTypeIdentity> active)
    {
        type = Unmodified(type);
        if (type.Shape is CliTypeShape.UnmanagedPointer or CliTypeShape.FunctionPointer)
        {
            return true;
        }
        if (type.Shape == CliTypeShape.Primitive)
        {
            return NativeAbiScalars.IsBlittable(type);
        }
        // What remains is a struct or enum, closed (a P/Invoke's signature has no generic parameters). A struct that
        // contains itself, which only malformed metadata has, is not blittable rather than walked forever.
        if (!type.IsValueType || !active.Add(type))
        {
            return false;
        }
        var definition = definitions.ResolveTypeIdentity(type);
        var blittable = definition.IsEnum
            ? NativeAbiScalars.IsBlittable(definition.EnumUnderlyingType)
            : definition.LayoutKind != CliTypeLayoutKind.Auto && definition.Fields.Select(fields.GetField)
                .Where(field => !field.IsStatic)
                .All(field => IsBlittable(field.SignatureType.Substitute(type.TypeArguments), active));
        active.Remove(type);
        return blittable;
    }

    // The compilation's value types, for a struct's native layout.


    private static CliTypeIdentity Unmodified(CliTypeIdentity type)
    {
        while (type.Shape == CliTypeShape.Modified)
        {
            type = type.ElementType!;
        }
        return type;
    }

    private static CompilerException Unsupported(string method, string message) =>
        new(new(DiagnosticCode.NativeInterop, message, method));
}
