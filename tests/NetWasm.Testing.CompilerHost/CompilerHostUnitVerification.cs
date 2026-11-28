// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Testing.CompilerHost;

internal interface ICompilerHostUnitVerification
{
    // Compiles the request again with compiled units under verification, when the environment asks for it, and fails
    // when units change the module. The module is the request's own compile.
    void Verify(CompilerOptions options, byte[] module);
}

/// <summary>
/// With <c>NETWASM_TEST_COMPILED_UNITS=verify</c> every request is also compiled twice at its own level with compiled
/// units under shadow verification, in a store shared with the other requests of the run. The first compile restores
/// what the store's previous compile left that still fits (a store keeps only the units its last compile used, and a
/// unit's input names the program's policy and assembly shapes, so another program's units seldom fit) and leaves this
/// program's units; the second restores those. Every restored method is emitted too and compared, each module must
/// equal the plain one, and the second compile must restore some unit when the first kept any, so every request checks
/// restoring and not only capturing. The plain module is the request's own compile unless the request itself used
/// compiled units. The store lives in one of a few slots under <c>NETWASM_TEST_COMPILED_UNITS_DIR</c> (default: the
/// temp directory), each locked for the length of a request's compiles, so parallel hosts never share one.
/// </summary>
internal sealed partial class CompilerHostUnitVerification(INetWasmCompiler compiler) : ICompilerHostUnitVerification
{
    private const int Slots = 8;
    private readonly INetWasmCompiler _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));

    public void Verify(CompilerOptions options, byte[] module)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(module);
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
        var plain = module;
        if (options.CompiledUnits || options.VerifyCompiledUnits)
        {
            try
            {
                plain = _compiler.Compile(plainOptions).ApplicationModule;
            }
            catch (CompilerException)
            {
                // The program does not compile; there is nothing to compare with.
                return;
            }
        }
        var root = Environment.GetEnvironmentVariable("NETWASM_TEST_COMPILED_UNITS_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetTempPath(), "netwasm-test-compiled-units");
        Directory.CreateDirectory(root);
        using var slot = Acquire(root);
        var unitOptions = plainOptions with
        {
            IntermediateOutputPath = slot.Directory,
            CompiledUnits = true,
            VerifyCompiledUnits = true,
        };
        var capturing = Compile(unitOptions, plain, options, slot, "capturing");
        var restoring = Compile(unitOptions, plain, options, slot, "restoring");
        if (capturing.Kept > 0 && restoring.Restored == 0)
        {
            throw new InvalidOperationException(
                $"Compiled units restored nothing of the {capturing.Kept} units the previous compile of the " +
                $"{options.Optimization} module of {options.EntryAssemblyPath} kept ({options.Target}); store " +
                $"{slot.Directory}: {restoring.Line}");
        }
    }

    // Compiles with units and compares the module with the plain one; returns the compile's reuse summary.
    private UnitSummary Compile(CompilerOptions unitOptions, byte[] plain, CompilerOptions options, UnitSlot slot,
        string pass)
    {
        var module = _compiler.Compile(unitOptions).ApplicationModule;
        if (!plain.AsSpan().SequenceEqual(module))
        {
            throw new InvalidOperationException(
                $"Compiled units ({pass}) changed the {options.Optimization} module of {options.EntryAssemblyPath} " +
                $"({options.Target}, stack traces {options.EmitStackTrace}); store {slot.Directory}.");
        }
        return UnitSummary.Read(slot.Directory);
    }

    // The first line of the store's last-compile.txt: "restored R emitted E kept K ...".
    private sealed record UnitSummary(int Restored, int Kept, string Line)
    {
        public static UnitSummary Read(string directory)
        {
            var path = Directory.EnumerateFiles(directory, "last-compile.txt", SearchOption.AllDirectories).Single();
            var line = File.ReadLines(path).First();
            var match = SummaryPattern().Match(line);
            if (!match.Success)
                throw new InvalidOperationException($"Unreadable compiled-units summary in {path}: {line}");
            return new(Count(match.Groups[1].Value), Count(match.Groups[2].Value), line);
        }

        private static int Count(string value) => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^restored (\d+) emitted \d+ kept (\d+) ")]
    private static partial System.Text.RegularExpressions.Regex SummaryPattern();

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
