using System.Linq;
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
        // The validators keep no state between methods, so methods are checked on all cores;
        // the first failing method in order is reported.
        var ordinary = program.Methods.ToArray();
        var constructed = program.ConstructedMethods.ToArray();
        OrderedParallel.For(ordinary.Length + constructed.Length, index =>
        {
            if (index < ordinary.Length)
            {
                var pair = ordinary[index];
                if (pair.Key != pair.Value.Header.Method.Key)
                    throw IdentityMismatch(symbols.Format(pair.Value.Header.Method));
                Validate(pair.Value);
            }
            else
            {
                var pair = constructed[index - ordinary.Length];
                if (pair.Value.Header.MethodInstance?.CanonicalName != pair.Key)
                    throw IdentityMismatch(pair.Key);
                Validate(pair.Value);
            }
        });
    }

    // An outline has no blocks yet; it is checked when it is structured, if it is emitted.
    private void Validate(StructuredMethod method)
    {
        if (method.Outline is null) _validator.Validate(method);
    }

    private CompilerException IdentityMismatch(string method) => exceptions.Create(
        "structured method identity does not match its reachable-program key",
        method);
}
