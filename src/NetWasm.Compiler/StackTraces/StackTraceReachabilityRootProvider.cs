using System;
using System.Linq;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.StackTraces;

internal sealed class StackTraceReachabilityRootProvider :
    IStackTraceReachabilityRootProvider
{
    public ReachabilityRoots Provide(
        bool enabled,
        ITypeFinder types,
        IFieldRepository fields)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fields);
        if (!enabled)
        {
            return ReachabilityRoots.Empty;
        }

        var exception = types.FindType("System.Exception");
        var stackTrace = exception.Fields
            .Select(fields.GetField)
            .Single(field => field.Name == "_stackTrace");
        var @string = types.FindType("System.String");
        return new([exception.Key, @string.Key], [stackTrace.Key], []);
    }
}
