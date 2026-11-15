// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.ModuleEncoding;

// The largest function engines load: the WebAssembly JS API's implementation limits, which V8, SpiderMonkey
// and JavaScriptCore share. Parameters count among a function's locals, as V8 and Wasmtime count them.
public sealed record WasmFunctionLimits(
    int MaximumBodySize = 7_654_321,
    int MaximumParameters = 1_000,
    int MaximumLocals = 50_000);

// Rejects functions an engine would refuse to compile; the validator after linking can only name a byte offset.
public interface IWasmFunctionLimitValidator
{
    void Validate(IReadOnlyList<WasmFunctionDefinition> functions);
}

// Reports the first function over a limit, as each was reported before. When every function over a limit is a
// method's and over its locals or body size, which inlining into it adds to, the diagnostic also names those
// methods, so the compile can be repeated with less inlining into them; a parameter count or a generated
// function cannot shrink, so it names none.
public sealed class WasmFunctionLimitValidator(WasmFunctionLimits limits) : IWasmFunctionLimitValidator
{
    private readonly WasmFunctionLimits _limits = limits ?? throw new ArgumentNullException(nameof(limits));

    public void Validate(IReadOnlyList<WasmFunctionDefinition> functions)
    {
        ArgumentNullException.ThrowIfNull(functions);
        CompilerDiagnostic? first = null;
        var methods = new List<EntityKey>();
        var recompilable = true;
        foreach (var function in functions)
        {
            if (Violation(function) is not { } violation)
            {
                continue;
            }
            first ??= violation.Diagnostic;
            if (violation.Recompilable && function.Method is { } method)
            {
                if (!methods.Contains(method)) methods.Add(method);
            }
            else
            {
                recompilable = false;
            }
        }
        if (first is not null)
        {
            throw new CompilerException(first with { LimitExceedingMethods = recompilable ? [.. methods] : [] });
        }
    }

    private (CompilerDiagnostic Diagnostic, bool Recompilable)? Violation(WasmFunctionDefinition function)
    {
        var parameters = function.Type.Parameters.Length;
        if (parameters > _limits.MaximumParameters)
        {
            return (Limit($"the method compiles to a WebAssembly function with {parameters:N0} parameters, over the " +
                $"{_limits.MaximumParameters:N0} that browsers, Node and Wasmtime accept; pass the values in fewer " +
                "parameters, for example in an array or an object", function.Name), false);
        }
        var locals = parameters + DeclaredLocals(function.Body);
        if (locals > _limits.MaximumLocals)
        {
            return (Limit($"the method compiles to a WebAssembly function with {locals:N0} locals, over the " +
                $"{_limits.MaximumLocals:N0} that browsers, Node and Wasmtime accept; split it into smaller methods",
                function.Name), true);
        }
        if (function.Body.Length > _limits.MaximumBodySize)
        {
            return (Limit($"the method compiles to a {function.Body.Length:N0}-byte WebAssembly function, over the " +
                $"{_limits.MaximumBodySize:N0}-byte limit that browsers and Node enforce; split it into smaller methods",
                function.Name), true);
        }
        return null;

        static CompilerDiagnostic Limit(string message, string method) =>
            new(DiagnosticCode.FunctionLimit, message, method);
    }

    // A body starts with its locals: a count of runs, each a count and a value type. A reference
    // type that names its heap type (0x63, 0x64) carries the heap type after it.
    private static long DeclaredLocals(byte[] body)
    {
        var position = 0;
        var runs = ReadUnsigned(body, ref position);
        long locals = 0;
        for (ulong run = 0; run < runs; run++)
        {
            locals += (long)ReadUnsigned(body, ref position);
            if (body[position++] is 0x63 or 0x64) ReadUnsigned(body, ref position);
        }
        return locals;
    }

    private static ulong ReadUnsigned(byte[] bytes, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; ; shift += 7)
        {
            var next = bytes[position++];
            value |= (ulong)(next & 0x7f) << shift;
            if ((next & 0x80) == 0) return value;
        }
    }
}
