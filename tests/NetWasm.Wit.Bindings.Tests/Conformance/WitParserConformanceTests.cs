using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Wit.Bindings.Tests.Conformance;

// wit-parser's own test inputs, at the wasm-tools version NetWasm pins, run
// through real wasm-tools. Every world of a valid document generates bindings
// that compile against CoreLib, unless it uses something NetWasm does not
// support, which stops with a diagnostic naming it; those worlds are listed
// below with the reason. Every invalid document stops with wasm-tools'
// diagnostic. No input ends in any other exception.
public sealed class WitParserConformanceTests
{
    // Input and world, by the world's specifier, to a part of the diagnostic.
    private static readonly ImmutableDictionary<string, string> Unsupported =
        new Dictionary<string, string>
        {
            ["async.wit test:async/y"] = "asynchronous WIT function 'x' in world 'y' is not supported",
            ["maps.wit foo:maps/maps-world"] = "unsupported named WIT type 'map'",
        }.ToImmutableDictionary();

    // wit-parser's tests read these with the `active` feature gate enabled;
    // NetWasm reads WIT with the default features, so wasm-tools rejects them.
    private static readonly ImmutableDictionary<string, string> NeedsFeatures =
        new Dictionary<string, string>
        {
            ["gated-include.wit"] = "excluded due to its feature not being activated",
            ["multi-package-gated-include.wit"] = "world 'imports' not found in package 'wasi:dep-unstable'",
        }.ToImmutableDictionary();

    private static readonly string Corpus = Path.Combine(
        BindingCompiler.RepositoryRoot(), "tests", "NetWasm.Wit.Bindings.Tests", "Conformance", "wit-parser", "ui");

    public static TheoryData<string> ValidInputs() =>
        [.. Directory.EnumerateFileSystemEntries(Corpus)
            .Select(static entry => Path.GetFileName(entry))
            .Where(static name => name != "parse-fail" && (name.EndsWith(".wit", StringComparison.Ordinal) ||
                name.EndsWith(".wat", StringComparison.Ordinal) || Directory.Exists(Path.Combine(Corpus, name))))
            .Order(StringComparer.Ordinal)];

    public static TheoryData<string> InvalidInputs() =>
        [.. Directory.EnumerateFileSystemEntries(Path.Combine(Corpus, "parse-fail"))
            .Select(static entry => Path.GetFileName(entry))
            .Where(static name => name.EndsWith(".wit", StringComparison.Ordinal) ||
                Directory.Exists(Path.Combine(Corpus, "parse-fail", name)))
            .Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(ValidInputs))]
    public void EveryWorldGeneratesBindingsThatCompileOrNamesWhatIsUnsupported(string input)
    {
        var path = Path.Combine(Corpus, input);
        var reader = BindingCompiler.Reader();
        if (NeedsFeatures.TryGetValue(input, out var gated))
        {
            Assert.Contains(gated, Assert.Throws<CompilerException>(() => reader.Read(path)).Message,
                StringComparison.Ordinal);
            return;
        }
        var document = reader.Read(path);
        var specifiers = new NetWasm.Compiler.ComponentModel.Worlds.WitWorldSpecifierFormatter();
        foreach (var world in document.Worlds)
        {
            var specifier = specifiers.Format(world);
            var key = input + " " + specifier;
            string source;
            try
            {
                source = WitBindingCompositionRoot.Create().Generate(document, world);
                // The component's view of the world, as the compiler reads it.
                _ = reader.Read(path, specifier);
            }
            catch (Exception exception) when (exception is WitBindingException or CompilerException)
            {
                Assert.True(Unsupported.TryGetValue(key, out var reason),
                    $"{key} was rejected: {exception.Message}");
                Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
                continue;
            }
            Assert.False(Unsupported.ContainsKey(key), $"{key} is listed as unsupported but generated");
            var errors = BindingCompiler.Compile(source);
            Assert.True(errors.Length == 0, $"{key}:\n{string.Join('\n', errors)}\n\n{source}");
        }
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public void EveryInvalidDocumentStopsWithTheParsersDiagnostic(string input)
    {
        var exception = Assert.Throws<CompilerException>(() =>
            BindingCompiler.Reader().Read(Path.Combine(Corpus, "parse-fail", input)));

        Assert.Contains("invalid WIT input: ", exception.Message, StringComparison.Ordinal);
    }
}
