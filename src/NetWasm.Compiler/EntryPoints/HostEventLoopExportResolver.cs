using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

// A JavaScript module (a raw library) has no asynchronous Main to run its event loop, so it
// exports CoreLib's host event loop, which its JavaScript host runs after each call into it.
internal sealed class HostEventLoopExportResolver : ICompilationExportResolver
{
    internal const string RunExport = "netwasm.events.run";
    internal const string CallbackExport = "netwasm.events.callback";
    private const string LoopNamespace = "System.Runtime.InteropServices";
    private const string LoopType = "HostEventLoop";

    public IReadOnlyList<CompilationExportCandidate> Resolve(
        MetadataCompilationSnapshot metadata,
        IMethodFinder methods,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.UseJavaScriptExportBoundary || options.EntryPointKind != CompilerEntryPointKind.Library)
        {
            return [];
        }

        var loop = metadata.Types.SingleOrDefault(type => type.Namespace == LoopNamespace && type.Name == LoopType) ??
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.InvalidEntryPoint,
                $"a JavaScript module requires CoreLib's '{LoopNamespace}.{LoopType}'"));
        var members = loop.Methods.ToHashSet();
        return
        [
            Export(RunExport, "Run", 0),
            Export(CallbackExport, "Callback", 3),
        ];

        CompilationExportCandidate Export(string name, string methodName, int parameterCount)
        {
            var method = metadata.Methods.Single(candidate => members.Contains(candidate.Key) &&
                candidate.Name == methodName &&
                candidate.Signature.ParameterSignatureTypes.Length == parameterCount);
            return new(
                new ProgramExport(name, method.Key),
                DiagnosticCode.InvalidCommandLine,
                $"duplicate Wasm export name '{name}'",
                null);
        }
    }
}
