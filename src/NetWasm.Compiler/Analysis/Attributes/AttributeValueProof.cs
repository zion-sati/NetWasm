// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

internal enum AttributeValueKind
{
    Unknown,
    TypeToken,
    Type,
    Null,
    Int32,
    // An assembly the compile knows: Assembly.GetExecutingAssembly() or typeof(T).Assembly.
    Assembly,
    // A module the compile knows, the one module of the assembly the proof names: typeof(T).Module or a known
    // assembly's ManifestModule.
    Module,
    // A System.Type (or null) the analysis cannot name: a parameter, local, field or return typed Type, a GetType()
    // result, or different types meeting at a join.
    RuntimeType,
    // An array whose elements are Types (or null): its static type is Type[], or an array of a class derived from Type.
    // An element read from it is a RuntimeType.
    RuntimeTypeArray,
    // An address of a location typed Type: a field's, a local's, an argument's or an element's address, or a ref local,
    // parameter or return. A value read through it is a RuntimeType.
    RuntimeTypeAddress,
}

internal readonly record struct AttributeValueProof(
    AttributeValueKind Kind,
    CliTypeIdentity? Type = null,
    int Integer = 0,
    AssemblyIdentity? Assembly = null)
{
    public bool? Condition => Kind switch
    {
        AttributeValueKind.Null => false,
        AttributeValueKind.Type or AttributeValueKind.Assembly or AttributeValueKind.Module => true,
        AttributeValueKind.Int32 => Integer != 0,
        _ => null,
    };
}
