using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal sealed class ManagedExecutableEntryPointValidationStrategy :
    IEntryPointValidationStrategy
{
    public CompilerEntryPointKind Kind => CompilerEntryPointKind.ManagedExecutable;

    public void Validate(
        MethodDefinitionModel entryPoint,
        ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(entryPoint);
        ArgumentNullException.ThrowIfNull(symbols);
        var signature = entryPoint.Signature;
        var asyncReturn = JavaScriptAsyncSignature.Classify(
            signature.ReturnSignatureType);
        var validAsyncReturn = asyncReturn.Kind == JavaScriptAsyncReturnKind.Task &&
                               (!asyncReturn.HasResult ||
                                asyncReturn.ResultType!.StackKind == CliValueKind.I4);
        var validReturn = signature.ReturnType is CliValueKind.Void or CliValueKind.I4 ||
                          validAsyncReturn;
        var validParameters = signature.ParameterSignatureTypes.Length == 0 ||
                              signature.ParameterSignatureTypes is [var parameter] &&
                              IsStringArray(parameter);
        if (!entryPoint.IsStatic || entryPoint.GenericArity != 0 ||
            !validReturn || !validParameters)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.InvalidEntryPoint,
                "managed executable entry point must be static void/int/Task/Task<int> Main() or Main(string[])",
                symbols.Format(entryPoint)));
        }
    }

    private static bool IsStringArray(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.SzArray &&
        type.ElementType is
    {
        FullName: "System.String",
    }
            or
    {
        CanonicalName: "primitive:string",
    };
}
