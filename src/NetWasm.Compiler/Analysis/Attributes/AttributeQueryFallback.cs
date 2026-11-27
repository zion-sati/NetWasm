// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Why a reached attribute query stays at its run-time path.
internal enum AttributeQueryFallbackKind
{
    // A broad filter selects an attribute of a type the compile knows that cannot be built here.
    UnbuildableAttribute,
    // The filter selects an attribute that cannot be built on one of the program's run-time types.
    UnbuildableRunTimeAttribute,
    // The filter is a Type the compile cannot name.
    RunTimeFilter,
    // The target is a Type known only at run time, and the filter takes every attribute (object, Attribute, an
    // interface).
    EveryAttribute,
    // The target is a member the compile cannot prove to be a Type: a method, property, field, event or constructor.
    MemberTarget,
    // The target is an ICustomAttributeProvider the compile cannot prove to be a Type, assembly or module.
    ProviderTarget,
    RunTimeAssembly,
    RunTimeModule,
    OpenTarget,
    // A target's own query whose filter is neither an attribute type, an interface nor object.
    NotAnAttributeFilter,
    // A delegate or function pointer (ldftn, ldvirtftn) to the query.
    Indirect,
    // A method handle to the query (ldtoken), as an expression tree or reflection takes it.
    MethodHandle,
    // An overload the compiler does not lower, such as one taking a ParameterInfo.
    UnsupportedOverload,
    Unresolved,
}

// AnsweringType: a class of the program's own (or ParameterInfo) that the receiver may be at run time and that answers
// the query itself; the query then throws only on the runtime's own reflection objects.
internal sealed record AttributeQueryFallback(AttributeQueryFallbackKind Kind, string? Detail = null)
{
    public CliTypeIdentity? AnsweringType { get; init; }
}
