// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal static class InteropTypeClassifier
{
    public static bool IsString(CliTypeIdentity type) =>
        type.CanonicalName == "primitive:string";

    public static bool IsValueArray(CliTypeIdentity type) =>
        JavaScriptValueArray.IsSupported(type);

    /// <summary>
    /// A string, number array, JSObject or handle value: a value that enters managed code by conversion. Each crosses
    /// as a host handle, except a number array, which the host stages in linear memory (<see cref="TransportKinds"/>).
    /// </summary>
    public static bool IsJavaScriptReference(CliTypeIdentity type) =>
        IsString(type) || IsValueArray(type) || IsJSObject(type) || IsHandleValue(type);

    /// <summary>
    /// An object, exception or array of references: a value CoreLib converts to and
    /// from a host handle to its JavaScript value, which the receiver releases.
    /// </summary>
    public static bool IsHandleValue(CliTypeIdentity type) => JavaScriptConversion.IsHandleValue(type);

    public static bool IsJSObject(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSObject";

    public static bool IsSubscription(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSSubscription";

    public static bool IsHostObject(CliTypeIdentity type) =>
        IsJSObject(type) || IsSubscription(type);

    /// <summary>A DateTime or DateTimeOffset: a value that crosses as Unix milliseconds, a JS Date.</summary>
    public static bool IsDate(CliTypeIdentity type) => JavaScriptConversion.IsDate(type);

    /// <summary>A nullable scalar or date: a value that crosses as its presence and its value.</summary>
    public static bool IsNullable(CliTypeIdentity type) => JavaScriptNullable.IsSupported(type);

    /// <summary>A date or nullable: a struct an export builds from its transport form.</summary>
    public static bool IsConvertedValue(CliTypeIdentity type) => IsDate(type) || IsNullable(type);

    /// <summary>
    /// The first Wasm local of each parameter's transport, counting from <paramref name="first"/>,
    /// followed by the first local after them: a nullable takes two, its presence and its value, and a number
    /// array two, its staged elements' address and its length.
    /// </summary>
    public static int[] TransportLocals(IReadOnlyList<CliTypeIdentity> parameters, int first)
    {
        var locals = new int[parameters.Count + 1];
        locals[0] = first;
        for (var index = 0; index < parameters.Count; index++)
            locals[index + 1] = locals[index] + (IsNullable(parameters[index]) || IsValueArray(parameters[index]) ? 2 : 1);
        return locals;
    }

    /// <summary>
    /// The Wasm values an export or callback parameter arrives as: a number array's elements, which the host copied
    /// into a block of linear memory from the runtime's allocator, as the block's address and the array's length (-1
    /// for null); another reference's host handle; a date's milliseconds; a nullable's presence and value; or the
    /// scalar.
    /// </summary>
    public static CliValueKind[] TransportKinds(CliTypeIdentity type, CliValueKind scalar) =>
        IsValueArray(type) ? [CliValueKind.NativeInt, CliValueKind.I4]
        : IsJavaScriptReference(type) ? [CliValueKind.I4]
        : IsDate(type) ? [CliValueKind.F8]
        : JavaScriptNullable.Underlying(type) is { } underlying
            ? [CliValueKind.I4, IsDate(underlying) ? CliValueKind.F8 : underlying.StackKind]
            : [scalar];

    /// <summary>The transport kind of a converted result: a date's milliseconds, a nullable's record address.</summary>
    public static CliValueKind ConvertedResultKind(CliTypeIdentity type) =>
        IsNullable(type) ? CliValueKind.ManagedAddress : CliValueKind.F8;

    /// <summary>A value an export converts rather than passing as it is: a reference, date or nullable.</summary>
    public static bool IsConverted(CliTypeIdentity type) => IsJavaScriptReference(type) || IsConvertedValue(type);
}
