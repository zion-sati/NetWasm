using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ModuleExportValidatorTests
{
    [Fact]
    public void AcceptsUniqueFinalExports()
    {
        var validator = CreateValidator();

        validator.Validate(
            [new("run", 1), new("memory", 0, WasmExportKind.Memory)],
            new Dictionary<string, int> { ["native_entry"] = 2 });
    }

    [Fact]
    public void RejectsNamedCallbackCollisionAfterComponentComposition()
    {
        var validator = CreateValidator();

        var exception = Assert.Throws<CompilerException>(() => validator.Validate(
            [new("initialize", 1), new("initialize", 2)],
            new Dictionary<string, int> { ["initialize"] = 1 }));

        Assert.Equal(DiagnosticCode.NativeInterop, exception.Diagnostic.Code);
    }

    [Fact]
    public void RejectsGeneralExportCollision()
    {
        var validator = CreateValidator();

        var exception = Assert.Throws<CompilerException>(() => validator.Validate(
            [new("duplicate", 1), new("duplicate", 2)],
            new Dictionary<string, int>()));

        Assert.Equal(DiagnosticCode.CompilerInvariant, exception.Diagnostic.Code);
    }

    [Fact]
    public void RejectsMissingInputs()
    {
        var validator = CreateValidator();
        IReadOnlyDictionary<string, int> nativeCallbacks =
            new Dictionary<string, int>();

        Assert.Throws<ArgumentNullException>(() =>
            validator.Validate(null!, nativeCallbacks));
        Assert.Throws<ArgumentNullException>(() =>
            validator.Validate([], null!));
    }

    private static IModuleExportValidator CreateValidator() =>
        new[] { new ModuleExportValidator() }
            .Cast<IModuleExportValidator>()
            .Single();
}
