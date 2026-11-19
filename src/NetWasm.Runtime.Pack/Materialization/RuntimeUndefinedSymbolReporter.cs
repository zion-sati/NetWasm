// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace NetWasm.Runtime.Pack.Materialization;

// wasm-ld names each function native code calls that nothing defines ("undefined symbol: glFoo"). Emscripten-built
// archives call functions Emscripten implements in JavaScript: NetWasm stands in for its GL functions, and raw
// WebAssembly output's JavaScript supplies any other once declared, while a component has no JavaScript.
internal sealed partial class RuntimeUndefinedSymbolReporter : IRuntimeUndefinedSymbolReporter
{
    public InvalidOperationException? Explain(string linkLog, bool rawWasmOutput, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(linkLog);
        ArgumentNullException.ThrowIfNull(failure);
        var names = UndefinedSymbol().Matches(linkLog).Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (names.Length == 0)
            return null;
        var listed = string.Join(", ", names.Select(name => $"'{name}'"));
        var advice = rawWasmOutput
            ? $"Declare <NetWasmEnvImport Include=\"{names[0]}\" Module=\"(the import module that supplies it)\" /> for " +
              "each function the app's JavaScript supplies, or link a native library that defines them."
            : "A component has no JavaScript: NetWasm supplies stand-ins only for Emscripten's GL functions and " +
              "emscripten_has_asyncify. Link a native library that defines them, or build the native library without them.";
        return new InvalidOperationException(
            $"Native code calls {listed}, which nothing in the program defines. {advice}", failure);
    }

    [GeneratedRegex(@"undefined symbol: (?<name>[^\s]+)")]
    private static partial Regex UndefinedSymbol();
}
