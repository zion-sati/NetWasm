using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

internal interface IPlainCoreModuleValidator
{
    void Validate(
        ReachableProgram program,
        ImmutableArray<ProgramExport> exports,
        PlatformWorldCapabilities world,
        IMethodRepository methods,
        ISymbolFormatter symbols);
}

// A plain core module imports only its world's functions, for engines without the Component
// Model: a reachable WASI import, Component Model built-in (an async import or the event loop)
// or async export is a build error that names the managed method reaching it.
internal sealed class PlainCoreModuleValidator : IPlainCoreModuleValidator
{
    public void Validate(
        ReachableProgram program,
        ImmutableArray<ProgramExport> exports,
        PlatformWorldCapabilities world,
        IMethodRepository methods,
        ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(symbols);
        if (world.Wasi)
        {
            return;
        }

        var import = program.WitImportMethods
            .Where(method => method.WitImport!.IsCanonicalBuiltIn ||
                method.WitImport.InterfaceName.StartsWith("wasi:", StringComparison.Ordinal))
            .OrderBy(symbols.Format, StringComparer.Ordinal)
            .FirstOrDefault();
        if (import is not null)
        {
            var binding = import.WitImport!;
            var name = $"'{binding.InterfaceName}#{binding.FunctionName}'";
            throw Invalid(
                binding.IsCanonicalBuiltIn
                    ? $"it reaches the Component Model built-in '{binding.FunctionName}', which needs component output (an async WIT import does)"
                    : binding.InterfaceName.StartsWith("wasi:http/", StringComparison.Ordinal)
                        ? $"it reaches the WASI import {name}; HTTP over wasi:http needs component output, so give HttpClient an HttpMessageHandler of your own, typically over the world's imports, or build a component"
                        : $"it reaches the WASI import {name}; a plain core module's system services come from its platform (NetWasmPlatformAttribute), so provide the service there, or build a component",
                symbols.Format(import));
        }

        var asyncExport = exports
            .Select(export => methods.GetMethod(export.Method))
            .Where(method => method.WitExport is { IsComponentLift: true })
            .OrderBy(symbols.Format, StringComparer.Ordinal)
            .FirstOrDefault();
        if (asyncExport is not null)
        {
            throw Invalid(
                $"it exports the async function '{asyncExport.WitExport!.FunctionName}'; async WIT functions need component output",
                symbols.Format(asyncExport));
        }
    }

    private static CompilerException Invalid(string reason, string method) => new(new CompilerDiagnostic(
        DiagnosticCode.ComponentContract,
        "a plain core module has no WASI and no Component Model: " + reason,
        method));
}
