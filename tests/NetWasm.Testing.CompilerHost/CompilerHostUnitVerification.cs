using NetWasm.Compiler;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Testing.CompilerHost;

internal interface ICompilerHostUnitVerification
{
    // Compiles the request again with compiled units under verification, when the
    // environment asks for it, and fails when units change the module.
    void Verify(CompilerOptions options);
}

/// <summary>
/// With <c>NETWASM_TEST_COMPILED_UNITS=verify</c> every request is also compiled twice at its own level:
/// plainly, and with compiled units under shadow verification in a store shared with the
/// other requests of the run. A request therefore restores the units another program,
/// target or option set left behind (CoreLib's methods most of all), each restored method
/// is emitted too and compared, and the module must equal the plain one. The store lives in
/// one of a few slots under <c>NETWASM_TEST_COMPILED_UNITS_DIR</c> (default: the temp
/// directory), each locked for the length of a compile, so parallel hosts never share one.
/// </summary>
internal sealed class CompilerHostUnitVerification(INetWasmCompiler compiler) : ICompilerHostUnitVerification
{
    private const int Slots = 8;
    private readonly INetWasmCompiler _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));

    public void Verify(CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (Environment.GetEnvironmentVariable("NETWASM_TEST_COMPILED_UNITS") != "verify" ||
            options.DiagnosticTracePath is not null)
        {
            return;
        }
        var plainOptions = options with
        {
            MetricsObserver = null,
            CompiledUnits = false,
            VerifyCompiledUnits = false,
        };
        byte[] plain;
        try
        {
            plain = _compiler.Compile(plainOptions).ApplicationModule;
        }
        catch (CompilerException)
        {
            // The program does not compile; there is nothing to compare with.
            return;
        }
        var root = Environment.GetEnvironmentVariable("NETWASM_TEST_COMPILED_UNITS_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetTempPath(), "netwasm-test-compiled-units");
        Directory.CreateDirectory(root);
        using var slot = Acquire(root);
        var restored = _compiler.Compile(plainOptions with
        {
            IntermediateOutputPath = slot.Directory,
            CompiledUnits = true,
            VerifyCompiledUnits = true,
        }).ApplicationModule;
        if (!plain.AsSpan().SequenceEqual(restored))
        {
            throw new InvalidOperationException(
                $"Compiled units changed the {options.Optimization} module of {options.EntryAssemblyPath} " +
                $"({options.Target}, stack traces {options.EmitStackTrace}); store {slot.Directory}.");
        }
    }

    private static UnitSlot Acquire(string root)
    {
        for (var attempt = 0; ; attempt++)
        {
            for (var index = 0; index < Slots; index++)
            {
                var directory = Path.Combine(root, $"slot-{index}");
                try
                {
                    var gate = new FileStream(directory + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                        FileShare.None);
                    Directory.CreateDirectory(directory);
                    return new UnitSlot(directory, gate);
                }
                catch (IOException)
                {
                    // Another host holds this slot.
                }
            }
            Thread.Sleep(Math.Min(200, 10 * (attempt + 1)));
        }
    }

    private sealed class UnitSlot(string directory, FileStream gate) : IDisposable
    {
        public string Directory { get; } = directory;

        public void Dispose() => gate.Dispose();
    }
}
