using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.ModuleEncoding;

namespace NetWasm.Compiler.Wasm.Tests;

// A function over an engine limit fails the module, reported as before by the first such function; the methods
// whose functions are over a locals or body-size limit are named for a recompile with less inlining into them,
// unless a function over a limit cannot shrink that way: one over the parameter limit, or a generated one.
public sealed class WasmFunctionLimitValidatorTests
{
    private static readonly WasmFunctionLimits Small = new(MaximumBodySize: 8, MaximumParameters: 2, MaximumLocals: 3);
    private static readonly EntityKey First = new(new AssemblyIdentity("App"), 0x06000001);
    private static readonly EntityKey Second = new(new AssemblyIdentity("App"), 0x06000002);

    [Fact]
    public void TheEnginesLimitsAreTheDefaults()
    {
        Assert.Equal(new WasmFunctionLimits(7_654_321, 1_000, 50_000), new WasmFunctionLimits());
    }

    [Fact]
    public void FunctionsAtTheLimitsPass()
    {
        new WasmFunctionLimitValidator(Small).Validate(
        [
            Function("Body", size: 8),
            Function("Locals", locals: 3),
            Function("Parameters", parameters: 2, locals: 1),
            Function("References", references: 3),
        ]);
    }

    [Fact]
    public void AMethodOverTheBodySizeIsNamedForARecompile()
    {
        var error = Validate(Function("App::Big", size: 9) with { Method = First });

        Assert.Equal("NW1013", error.Diagnostic.Id);
        Assert.Equal("App::Big", error.Diagnostic.Method);
        Assert.Equal("the method compiles to a 9-byte WebAssembly function, over the 8-byte limit that browsers and " +
            "Node enforce; split it into smaller methods", error.Diagnostic.Message);
        Assert.Equal([First], error.Diagnostic.LimitExceedingMethods);
    }

    // Locals and references naming their heap type count alike, parameters among them.
    [Fact]
    public void AMethodOverTheLocalLimitIsNamedForARecompile()
    {
        var error = Validate(Function("App::Wide", parameters: 1, locals: 1, references: 2) with { Method = First });

        Assert.Equal("the method compiles to a WebAssembly function with 4 locals, over the 3 that browsers, Node and " +
            "Wasmtime accept; split it into smaller methods", error.Diagnostic.Message);
        Assert.Equal([First], error.Diagnostic.LimitExceedingMethods);
    }

    // The first function over a limit is reported; each method over one is named once, in order, though several
    // functions of one method (its instantiations) are over.
    [Fact]
    public void EveryMethodOverALimitIsNamedOnce()
    {
        var error = Validate(
            Function("App::Fits", size: 2) with { Method = Second },
            Function("App::First<int>", size: 9) with { Method = First },
            Function("App::Second", locals: 4) with { Method = Second },
            Function("App::First<long>", size: 10) with { Method = First });

        Assert.Equal("App::First<int>", error.Diagnostic.Method);
        Assert.Equal([First, Second], error.Diagnostic.LimitExceedingMethods);
    }

    [Fact]
    public void AGeneratedFunctionOverALimitNamesNoMethod()
    {
        var error = Validate(
            Function("App::Big", size: 9) with { Method = First },
            Function("generated", size: 9));

        Assert.Equal("App::Big", error.Diagnostic.Method);
        Assert.Empty(error.Diagnostic.LimitExceedingMethods);
    }

    [Fact]
    public void AFunctionOverTheParameterLimitNamesNoMethod()
    {
        var error = Validate(
            Function("App::Many", parameters: 3) with { Method = First },
            Function("App::Big", size: 9) with { Method = Second });

        Assert.Equal("App::Many", error.Diagnostic.Method);
        Assert.Equal("the method compiles to a WebAssembly function with 3 parameters, over the 2 that browsers, Node " +
            "and Wasmtime accept; pass the values in fewer parameters, for example in an array or an object",
            error.Diagnostic.Message);
        Assert.Empty(error.Diagnostic.LimitExceedingMethods);
    }

    [Fact]
    public void ItRejectsMissingInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmFunctionLimitValidator(null!));
        Assert.Throws<ArgumentNullException>(() => new WasmFunctionLimitValidator(Small).Validate(null!));
    }

    private static CompilerException Validate(params WasmFunctionDefinition[] functions) =>
        Assert.Throws<CompilerException>(() => new WasmFunctionLimitValidator(Small).Validate(functions));

    // A body of the given size, or one declaring the given i32 locals and references to a named heap type.
    private static WasmFunctionDefinition Function(string name, int size = 2, int parameters = 0, int locals = 0,
        int references = 0)
    {
        var body = new List<byte> { (byte)((locals > 0 ? 1 : 0) + (references > 0 ? 1 : 0)) };
        if (locals > 0) body.AddRange([(byte)locals, 0x7f]);
        if (references > 0) body.AddRange([(byte)references, 0x64, 0x70]);
        body.Add(0x0b);
        while (body.Count < size) body.Insert(body.Count - 1, 0x01);
        return new(name, new([.. Enumerable.Repeat(CliValueKind.I4, parameters)], CliValueKind.Void), [.. body]);
    }
}
