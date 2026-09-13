using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Validation;

internal sealed class StructuredProgramInvariantValidator(
    ICompilerInvariantExceptionFactory exceptions,
    IStructuredMethodValidatorFactory validators) :
    IStructuredProgramInvariantValidator
{
    private readonly IStructuredMethodValidator _validator =
        (validators ?? throw new ArgumentNullException(nameof(validators))).Create();

    public void Validate(ISymbolFormatter symbols, WasmMethodLoweringResult program)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(program);
        foreach (var pair in program.Methods)
        {
            ValidateIdentity(
                pair.Key == pair.Value.Header.Method.Key,
                symbols.Format(pair.Value.Header.Method));
            _validator.Validate(pair.Value);
        }
        foreach (var pair in program.ConstructedMethods)
        {
            ValidateIdentity(
                pair.Value.Header.MethodInstance?.CanonicalName == pair.Key,
                pair.Key);
            _validator.Validate(pair.Value);
        }
    }

    private void ValidateIdentity(bool valid, string method)
    {
        if (!valid)
        {
            throw exceptions.Create(
                "structured method identity does not match its reachable-program key",
                method);
        }
    }
}
