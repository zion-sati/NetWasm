using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record RuntimeJitWhitelist(
    int SchemaVersion,
    string Repository,
    string Commit,
    RuntimeJitLicense License,
    ImmutableArray<RuntimeJitEntry> Entries);

internal sealed record RuntimeJitLicense(
    string Spdx,
    string UpstreamPath,
    string Sha256,
    string VendoredPath);

internal sealed record RuntimeJitEntry(
    string Id,
    string Cohort,
    string UpstreamPath,
    string Sha256,
    string VendoredPath,
    string EntryType,
    string EntryMethod,
    bool ReturnsVoid,
    string Adaptation,
    ImmutableArray<string> RequiredFeatures,
    string OracleMode,
    string Disposition,
    string? Rationale);

internal static class RuntimeJitCorpus
{
    private static readonly Regex FileScopedNamespace = new(
        @"(?m)^namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static (RuntimeJitWhitelist Manifest, string Root) Load()
    {
        var root = CompilerCorrectnessEnvironment.Discover().RepositoryRoot;
        var path = Path.Combine(
            root,
            "compiler-qualification",
            "runtime-jit-whitelist.json");
        var manifest = JsonSerializer.Deserialize<RuntimeJitWhitelist>(
            File.ReadAllText(path),
            SerializerOptions)
            ?? throw new InvalidOperationException("runtime JIT whitelist was empty");
        return (manifest, root);
    }

    public static CorpusFixture CreateFixture(string root, RuntimeJitEntry entry)
    {
        var upstream = File.ReadAllText(Path.Combine(root, entry.VendoredPath))
            .ReplaceLineEndings("\n");
        var adapted = "#nullable disable\n" + upstream
            .Replace("using Xunit;", string.Empty, StringComparison.Ordinal)
            .Replace("using Microsoft.DotNet.XUnitExtensions;", string.Empty,
                StringComparison.Ordinal)
            .Replace("[Fact]", string.Empty, StringComparison.Ordinal)
            .Replace("[OuterLoop]", string.Empty, StringComparison.Ordinal)
            .Replace("System.IO.StringWriter", "TestUtil.ExpectedLog",
                StringComparison.Ordinal);
        var namespaceMatch = FileScopedNamespace.Match(adapted);
        var wrapperNamespace = namespaceMatch.Success
            ? namespaceMatch.Groups[1].Value
            : "NetWasm.Correctness.RuntimeJit." + entry.Id.Replace('-', '_');
        var harnessAdapter = """

            internal static class Console
            {
                public static void WriteLine() => TestUtil.TestLog.Record("");
                public static void WriteLine<T>(T value) =>
                    TestUtil.TestLog.Record(value?.ToString() ?? "");
                public static void WriteLine(string format, params object[] args) =>
                    TestUtil.TestLog.Record(format);
            }

            internal static class TestUtil
            {
                internal sealed class ExpectedLog
                {
                    private string _value = "";
                    public void WriteLine(string value) => _value += value + "\n";
                    public override string ToString() => _value;
                }

                internal sealed class TestLog : System.IDisposable
                {
                    private static TestLog _active;
                    private readonly string _expected;
                    private string _actual = "";
                    public TestLog() { }
                    public TestLog(string value) => _expected = value;
                    public TestLog(ExpectedLog value) => _expected = value.ToString();
                    public void StartRecording() => _active = this;
                    public void StopRecording() => _active = null;
                    public int VerifyOutput() => _actual == _expected ? 100 : 1;
                    public int VerifyOutput(string value) => _actual == value ? 100 : 1;
                    public static void Record(string value)
                    {
                        if (_active != null) _active._actual += value + "\n";
                    }
                    public void Dispose() { }
                }
            }

            """;
        var usesAssert = adapted.Contains("Assert.", StringComparison.Ordinal);
        if (usesAssert)
        {
            harnessAdapter += """

                internal static class Assert
                {
                    public static bool Failed { get; private set; }

                    public static void Equal<T>(T expected, T actual)
                    {
                        if (!object.Equals(expected, actual))
                        {
                            Failed = true;
                        }
                    }
                }
                """;
        }
        var invocation = entry.ReturnsVoid
            ? $$"""
                global::{{entry.EntryType}}.{{entry.EntryMethod}}();
                        _trace = {{(usesAssert ? "global::Assert.Failed ? 1 : 100" : "100")}};
                """
            : $$"""_trace = global::{{entry.EntryType}}.{{entry.EntryMethod}}();""";
        var wrapper = $$"""

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    {{invocation}}
                    return _trace;
                }

                public static int Trace() => _trace;
            }
            """;
        adapted += namespaceMatch.Success
            ? harnessAdapter + wrapper
            : $$"""

                {{harnessAdapter}}

                namespace {{wrapperNamespace}}
                {
                {{Indent(wrapper, 4)}}
                }
                """;
        adapted = adapted.ReplaceLineEndings("\n");
        return new(entry.Id, wrapperNamespace, adapted, [0])
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
        };
    }

    private static string Indent(string value, int spaces)
    {
        var indentation = new string(' ', spaces);
        return string.Join(
            "\n",
            value.ReplaceLineEndings("\n").Split('\n')
                .Select(line => indentation + line));
    }
}
