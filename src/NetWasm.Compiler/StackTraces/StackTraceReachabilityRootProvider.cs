// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Linq;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.StackTraces;

// Stack traces keep Exception._stackTrace and String, and CoreLib's method-symbols loader, which the program runs at
// startup (issue 225); a program without them pays nothing. Type names root nothing: CoreLib's name readers reach the
// type-symbols loader, so only a program that reads a name carries it.
internal sealed class StackTraceReachabilityRootProvider :
    IStackTraceReachabilityRootProvider
{
    public ReachabilityRoots Provide(
        bool stackTraces,
        ITypeFinder types,
        IFieldRepository fields,
        IMethodRepository methods)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(methods);
        if (!stackTraces)
        {
            return ReachabilityRoots.Empty;
        }
        var exception = types.FindType("System.Exception");
        var stackTrace = exception.Fields
            .Select(fields.GetField)
            .Single(field => field.Name == "_stackTrace");
        var @string = types.FindType("System.String");
        var sidecars = types.FindType("System.Runtime.InteropServices.ProgramSidecars");
        var loader = sidecars.Methods.Single(method => methods.GetMethod(method).Name == "LoadMethodSymbols");
        return new([exception.Key, @string.Key], [stackTrace.Key], [loader]);
    }
}
