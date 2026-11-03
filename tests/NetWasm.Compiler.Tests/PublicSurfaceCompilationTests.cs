using System.Text.RegularExpressions;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Every public member of NetWasm.CoreLib's public types that compiles
// unoptimized compiles at every preset into a module that validates, so no
// optimization breaks code that only some program reaches.
// Unoptimized, a call outside NetWasm's profile is reported as unsupported and
// left out; any other failure, at any preset, fails the test.
public sealed partial class PublicSurfaceCompilationTests
{
    private const int Chunks = 16;

    private static readonly FinalWasmOptimization[] Presets =
    [
        FinalWasmOptimization.O0, FinalWasmOptimization.O1, FinalWasmOptimization.O2,
        FinalWasmOptimization.O3, FinalWasmOptimization.Os, FinalWasmOptimization.Oz,
    ];

    // What NetWasm reports for code outside its profile, whatever the preset.
    private static readonly DiagnosticCode[] ProfileBoundaries =
    [
        DiagnosticCode.UnsupportedCil, DiagnosticCode.UnsupportedMetadata,
        DiagnosticCode.NativeInterop, DiagnosticCode.GenericExpansion,
    ];

    public static TheoryData<int> ChunkNumbers() => [.. Enumerable.Range(0, Chunks)];

    [Theory]
    [MemberData(nameof(ChunkNumbers))]
    public void EveryPublicCoreLibMemberCompilesAtEveryPreset(int chunk)
    {
        using var assets = TestAssets.Create();
        var calls = PublicSurfaceProgram.Calls(assets.CoreLib, chunk, Chunks).ToList();
        var outside = new List<string>();
        while (Compile(assets, calls, FinalWasmOptimization.None) is { } failure)
        {
            var call = Culprit(assets, calls, FinalWasmOptimization.None, failure);
            Assert.True(failure.Diagnostic is { } diagnostic && ProfileBoundaries.Contains(diagnostic.Code),
                $"unoptimized, {calls[call]}{Environment.NewLine}{failure.Message}");
            outside.Add($"{calls[call]} -> {failure.Message}");
            calls.RemoveAt(call);
        }
        Assert.NotEmpty(calls);

        foreach (var preset in Presets)
        {
            if (Compile(assets, calls, preset) is { } failure)
            {
                Assert.Fail($"{preset}, {calls[Culprit(assets, calls, preset, failure)]}{Environment.NewLine}{failure.Message}");
            }
        }
    }

    private sealed record Failure(string Message, CompilerDiagnostic? Diagnostic, int? Call);

    // The program's compilation at a preset, validated, or how it failed.
    private static Failure? Compile(TestAssets assets, IReadOnlyList<string> calls, FinalWasmOptimization preset)
    {
        var program = PublicSurfaceProgram.Compile(assets.CoreLib, $"PublicSurface{calls.Count}", calls);
        var path = Path.Combine(assets.Directory, program.AssemblyName + ".dll");
        using (var output = File.Create(path))
        {
            var emitted = program.Emit(output, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        }
        try
        {
            // As an application compiles, against the platform world its imports come from.
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                path, [assets.CoreLib], "EntryPoint", "Run", [],
                WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
                WitWorld: "netwasm:platform/platform@1.0.0",
                Optimization: preset));
            ValidateWithNode(result.ApplicationModule, assets.Directory);
            return null;
        }
        catch (CompilerException exception)
        {
            return new(exception.Message, exception.Diagnostic, CallIn(exception.Diagnostic.Method));
        }
        catch (Exception exception) when (exception is not Xunit.Sdk.XunitException)
        {
            return new(exception.ToString(), null, null);
        }
    }

    // The call whose method the diagnostic names, or, when it names a member
    // the calls reach or the compiler failed otherwise, the first call that
    // fails the same way, found by halving.
    private static int Culprit(
        TestAssets assets, List<string> calls, FinalWasmOptimization preset, Failure failure)
    {
        if (failure.Call is { } call)
        {
            return call;
        }
        var (start, count) = (0, calls.Count);
        while (count > 1)
        {
            var half = count / 2;
            var first = calls.Skip(start).Take(half).ToArray();
            if (Compile(assets, first, preset) is { } again && Same(again, failure))
            {
                count = half;
            }
            else
            {
                (start, count) = (start + half, count - half);
            }
        }
        return start;
    }

    private static bool Same(Failure first, Failure second) =>
        first.Diagnostic is { } diagnostic
            ? diagnostic.Code == second.Diagnostic?.Code
            : second.Diagnostic is null &&
                FirstLine(first.Message) == FirstLine(second.Message);

    private static string FirstLine(string text) => text.Split('\n', 2)[0];

    private static int? CallIn(string? method) =>
        method is not null && SweepMethod().Match(method) is { Success: true } match
            ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    [GeneratedRegex(@"^EntryPoint::S(\d+)$")]
    private static partial Regex SweepMethod();
}
