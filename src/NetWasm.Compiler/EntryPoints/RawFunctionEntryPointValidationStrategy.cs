using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal sealed class RawFunctionEntryPointValidationStrategy :
    IEntryPointValidationStrategy
{
    public CompilerEntryPointKind Kind => CompilerEntryPointKind.RawFunction;

    public void Validate(
        MethodDefinitionModel entryPoint,
        ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(entryPoint);
        ArgumentNullException.ThrowIfNull(symbols);
        var valid = entryPoint.IsStatic &&
                    entryPoint.Signature.ReturnType == CliValueKind.I4 &&
                    entryPoint.Signature.ParameterTypes.AsSpan().SequenceEqual(
                        [CliValueKind.I4]);
        if (!valid)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.InvalidEntryPoint,
                "raw entry point must be static int Method(int)",
                symbols.Format(entryPoint)));
        }
    }
}
