using System;
using System.Linq;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.StackTraces;

// Stack traces keep Exception._stackTrace and String, and CoreLib's method-symbols loader; type names keep its
// type-symbols loader. The program runs each loader at startup (issue 225); a program without them pays nothing.
internal sealed class StackTraceReachabilityRootProvider :
    IStackTraceReachabilityRootProvider
{
    public ReachabilityRoots Provide(
        bool stackTraces,
        bool typeNames,
        ITypeFinder types,
        IFieldRepository fields,
        IMethodRepository methods)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(methods);
        var roots = ReachabilityRoots.Empty;
        if (stackTraces)
        {
            var exception = types.FindType("System.Exception");
            var stackTrace = exception.Fields
                .Select(fields.GetField)
                .Single(field => field.Name == "_stackTrace");
            var @string = types.FindType("System.String");
            roots = new([exception.Key, @string.Key], [stackTrace.Key], []);
        }
        if (typeNames)
        {
            roots = roots with { Methods = roots.Methods.Add(Loader(types, methods, "LoadTypeSymbols")) };
        }
        if (stackTraces)
        {
            roots = roots with { Methods = roots.Methods.Add(Loader(types, methods, "LoadMethodSymbols")) };
        }
        return roots;
    }

    private static EntityKey Loader(ITypeFinder types, IMethodRepository methods, string name)
    {
        var sidecars = types.FindType("System.Runtime.InteropServices.ProgramSidecars");
        return sidecars.Methods.Single(method => methods.GetMethod(method).Name == name);
    }
}
