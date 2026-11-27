// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Attributes;

// Builder: the words of an NW1019 report. The reference as a person reads it (the call, its target and filter where the
// compile knows them), why the compile did not answer it, and what would let it.
internal sealed class UnplannedAttributeQueryDescriber(ISymbolFormatter symbols) : IUnplannedAttributeQueryDescriber
{
    private readonly ISymbolFormatter _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));

    public UnplannedAttributeQuery Describe(ManagedMethodBody holder, AttributeQueryReference reference,
        AttributeQueryFallback fallback)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(fallback);
        var (reason, remedy) = Explain(fallback);
        return new(_symbols.Format(holder.Method.Definition), reference.Instruction.SourceOffset, Query(reference),
            reason, Outcome(fallback), remedy);
    }

    private static string Outcome(AttributeQueryFallback fallback) => fallback.AnsweringType is { } answering
        ? "the query throws PlatformNotSupportedException when its target is one of the runtime's own reflection " +
          $"objects, not an object that answers it itself, such as the program's {Name(answering)}"
        : "the query throws PlatformNotSupportedException when reached";

    private string Query(AttributeQueryReference reference)
    {
        var method = _symbols.Format(reference.Method.Definition);
        switch (reference.Instruction.Operation)
        {
            case CilOperation.LoadFunction or CilOperation.LoadVirtualFunction:
                return $"makes a delegate or function pointer of {method}";
            case CilOperation.LoadMethodToken:
                return $"loads the method handle of {method}";
        }
        if (reference is not { Call: { } call, Stack: { } stack })
        {
            return $"calls {method}";
        }
        var start = stack.Length - call.ArgumentCount;
        var target = stack[start] switch
        {
            { Kind: AttributeValueKind.Type, Type: { } type } => $" on {Name(type)}",
            { Kind: AttributeValueKind.RuntimeType } => " on a Type known only at run time",
            { Kind: AttributeValueKind.Assembly, Assembly: { } assembly } => $" on assembly {assembly.Name}",
            { Kind: AttributeValueKind.Module, Assembly: { } module } => $" on the module of {module.Name}",
            _ => "",
        };
        var filter = call.GenericFilter ??
            (call.FilterArgument is { } index
                ? stack[start + index] is { Kind: AttributeValueKind.Type, Type: { } exact } ? exact : null
                : call.ImplicitFilter);
        return $"calls {method}{target}{(filter is null ? "" : $" with filter {Name(filter)}")}";
    }

    private static string Name(CliTypeIdentity type) => type.FullName ?? type.CanonicalName;

    private static (string Reason, string Remedy) Explain(AttributeQueryFallback fallback) => fallback.Kind switch
    {
        AttributeQueryFallbackKind.UnbuildableAttribute => (
            $"it may select an attribute NetWasm cannot build: {fallback.Detail}",
            "Filter by the sealed attribute type the code needs, or fix the attribute the reason names."),
        AttributeQueryFallbackKind.UnbuildableRunTimeAttribute => (
            $"an attribute it selects on a type the program has at run time cannot be built: {fallback.Detail}",
            "Fix the attribute the reason names."),
        AttributeQueryFallbackKind.RunTimeFilter => (
            "its filter is not a Type the compile knows",
            "Pass the attribute type with typeof, or use the generic overload."),
        AttributeQueryFallbackKind.EveryAttribute => (
            "its target is a Type known only at run time and its filter takes every attribute, which would build " +
            "every attribute of every type the program has",
            "Filter by the attribute type the code needs."),
        AttributeQueryFallbackKind.MemberTarget => (
            "its target is a member the compile cannot prove to be a Type; NetWasm answers attribute queries on types, " +
            "assemblies and modules",
            "Query the Type itself."),
        AttributeQueryFallbackKind.ProviderTarget => (
            "its target is an ICustomAttributeProvider the compile cannot prove to be a Type, an assembly or a module",
            "Query the Type, assembly or module itself."),
        AttributeQueryFallbackKind.RunTimeAssembly => (
            "its target is an assembly known only at run time",
            "Query typeof(T).Assembly or Assembly.GetExecutingAssembly()."),
        AttributeQueryFallbackKind.RunTimeModule => (
            "its target is a module known only at run time",
            "Query typeof(T).Module."),
        AttributeQueryFallbackKind.OpenTarget => (
            "its target is an open generic type",
            "Query a constructed type."),
        AttributeQueryFallbackKind.NotAnAttributeFilter => (
            "its filter is not an attribute type, an interface or object",
            "Filter by an attribute type."),
        AttributeQueryFallbackKind.Indirect => (
            "a delegate or function pointer hides its target and filter from the compile",
            "Call the query directly."),
        AttributeQueryFallbackKind.MethodHandle => (
            "a method handle, as an expression tree or reflection uses it, hides its target and filter from the compile",
            "Call the query directly."),
        AttributeQueryFallbackKind.UnsupportedOverload => (
            "NetWasm answers attribute queries on types, assemblies and modules, and this overload takes another target",
            "Query a type, an assembly or a module."),
        _ => (
            "the compile cannot resolve its target and filter",
            "Pass a target and a filter the compile knows."),
    };
}
