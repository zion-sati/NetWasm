// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// The CoreLib members a marshalling stub calls and the handle fields it reads and writes, resolved once per compilation
// and only when a stub is built.
internal sealed class NativeMarshallingMembers
{
    public const string SafeHandleName = "System.Runtime.InteropServices.SafeHandle";
    public const string CriticalHandleName = "System.Runtime.InteropServices.CriticalHandle";
    public const string HandleRefName = "System.Runtime.InteropServices.HandleRef";

    private readonly IMethodRepository _methods;
    private readonly IFieldRepository _fields;
    private readonly Lazy<ITypeFinder> _types;
    private readonly Lazy<MethodInstanceModel> _safeHandleAddRef;
    private readonly Lazy<MethodInstanceModel> _safeHandleRelease;
    private readonly Lazy<MethodInstanceModel> _keepAlive;
    private readonly Lazy<MethodInstanceModel> _handleRefHandle;
    private readonly Lazy<MethodInstanceModel> _handleRefWrapper;
    private readonly Lazy<MethodInstanceModel> _arrayData;
    private readonly Lazy<MethodInstanceModel> _ansiCharToNative;
    private readonly Lazy<MethodInstanceModel> _ansiCharToManaged;
    private readonly Lazy<MethodInstanceModel> _stringToUtf8;
    private readonly Lazy<MethodInstanceModel> _freeNative;
    private readonly Lazy<MethodInstanceModel> _stringPinnableReference;
    private readonly Lazy<MethodInstanceModel> _ptrToStringUtf8;
    private readonly Lazy<MethodInstanceModel> _ptrToStringUtf16;
    private readonly Lazy<NativeHandleFields> _handleFields;

    public NativeMarshallingMembers(
        ITypeFinderFactory types,
        MetadataCompilationSnapshot snapshot,
        IMethodRepository methods,
        IFieldRepository fields)
    {
        _methods = methods;
        _fields = fields;
        _types = new(() => types.Create(snapshot));
        _safeHandleAddRef = new(() => Method("System.StubHelpers", "SafeHandleAddRef"));
        _safeHandleRelease = new(() => Method("System.StubHelpers", "SafeHandleRelease"));
        _keepAlive = new(() => Method("System.GC", "KeepAlive"));
        _handleRefHandle = new(() => Method(HandleRefName, "get_Handle"));
        _handleRefWrapper = new(() => Method(HandleRefName, "get_Wrapper"));
        _arrayData = new(() => Method("System.Runtime.InteropServices.MemoryMarshal", "GetArrayDataReference", genericArity: 0));
        _handleFields = new(() => new(Field(SafeHandleName, "handle"), Field(CriticalHandleName, "handle")));
        _ansiCharToNative = new(() => Method("System.StubHelpers", "AnsiCharToNative"));
        _ansiCharToManaged = new(() => Method("System.StubHelpers", "AnsiCharToManaged"));
        _stringToUtf8 = new(() => Method("System.Runtime.InteropServices.Marshal", "StringToCoTaskMemUTF8"));
        _freeNative = new(() => Method("System.Runtime.InteropServices.Marshal", "FreeCoTaskMem"));
        _stringPinnableReference = new(() => Method("System.String", "GetPinnableReference"));
        _ptrToStringUtf8 = new(() => Method("System.Runtime.InteropServices.Marshal", "PtrToStringUTF8", parameterCount: 1));
        _ptrToStringUtf16 = new(() => Method("System.Runtime.InteropServices.Marshal", "PtrToStringUni", parameterCount: 1));
    }

    // StubHelpers.SafeHandleAddRef(SafeHandle, ref bool) and SafeHandleRelease(SafeHandle).
    public MethodInstanceModel SafeHandleAddRef => _safeHandleAddRef.Value;
    public MethodInstanceModel SafeHandleRelease => _safeHandleRelease.Value;

    // GC.KeepAlive(object).
    public MethodInstanceModel KeepAlive => _keepAlive.Value;

    // HandleRef's Handle and Wrapper getters.
    public MethodInstanceModel HandleRefHandle => _handleRefHandle.Value;
    public MethodInstanceModel HandleRefWrapper => _handleRefWrapper.Value;

    // MemoryMarshal.GetArrayDataReference(Array): the first element's address, also for an empty array.
    public MethodInstanceModel ArrayDataReference => _arrayData.Value;

    // StubHelpers.AnsiCharToNative(char, bool bestFit, bool throwOnUnmappableChar) and AnsiCharToManaged(byte).
    public MethodInstanceModel AnsiCharToNative => _ansiCharToNative.Value;
    public MethodInstanceModel AnsiCharToManaged => _ansiCharToManaged.Value;

    // Marshal.StringToCoTaskMemUTF8(string) and FreeCoTaskMem(IntPtr): a string argument's UTF-8 copy, and its release.
    public MethodInstanceModel StringToUtf8 => _stringToUtf8.Value;
    public MethodInstanceModel FreeNative => _freeNative.Value;

    // String.GetPinnableReference(): a UTF-16 string argument's first character, which the stub pins.
    public MethodInstanceModel StringPinnableReference => _stringPinnableReference.Value;

    // Marshal.PtrToStringUTF8(IntPtr) and PtrToStringUni(IntPtr): a string result decoded from its native buffer.
    public MethodInstanceModel PtrToStringUtf8 => _ptrToStringUtf8.Value;
    public MethodInstanceModel PtrToStringUtf16 => _ptrToStringUtf16.Value;

    // The handle fields of SafeHandle and CriticalHandle.
    public NativeHandleFields HandleFields => _handleFields.Value;

    private MethodInstanceModel Method(string typeName, string name, int? genericArity = null, int? parameterCount = null)
    {
        var type = _types.Value.FindType(typeName);
        var method = type.Methods.Select(_methods.GetMethod)
            .Single(method => method.Name == name && (genericArity is null || method.GenericArity == genericArity) &&
                (parameterCount is null || method.Signature.ParameterTypes.Length == parameterCount));
        return new(method, CliTypeIdentity.FromDefinition(type), [], method.Signature);
    }

    private FieldInstanceModel Field(string typeName, string name)
    {
        var type = _types.Value.FindType(typeName);
        var field = type.Fields.Select(_fields.GetField).Single(field => field.Name == name && !field.IsStatic);
        return new(field, CliTypeIdentity.FromDefinition(type), field.SignatureType);
    }
}

// The handle field of each handle family.
internal sealed record NativeHandleFields(FieldInstanceModel Safe, FieldInstanceModel Critical)
{
    public FieldInstanceModel For(HandleKind kind) => kind == HandleKind.Safe ? Safe : Critical;
}

// The handle families desktop .NET's runtime marshalling passes as their handle value.
internal enum HandleKind
{
    Safe,
    Critical,
}
