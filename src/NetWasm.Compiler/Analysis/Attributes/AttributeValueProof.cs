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
    // A System.Type (or null) the analysis cannot name: a parameter, local, field or return typed Type, a GetType()
    // result, or different types meeting at a join.
    RuntimeType,
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
        AttributeValueKind.Type or AttributeValueKind.Assembly => true,
        AttributeValueKind.Int32 => Integer != 0,
        _ => null,
    };
}
