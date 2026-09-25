using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed partial class CilOpcodeUniverseTests
{
    [Fact]
    public void EveryRuntimeDefinedCilOpcodeHasAnExplicitSupportPolicy()
    {
        var repository = RepositoryRoot();
        var decoderSource = File.ReadAllText(Path.Combine(
            repository, "src/NetWasm.Compiler.Metadata/CilDecoder.cs"));
        var decoded = DecoderCase().Matches(decoderSource)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        using var policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repository, "compiler-qualification/cil-opcode-policy.json")));
        Assert.Equal(1, policy.RootElement.GetProperty("schemaVersion").GetInt32());
        var unsupportedEntries = policy.RootElement.GetProperty("unsupported")
            .EnumerateArray().ToArray();
        var unsupported = unsupportedEntries
            .Select(entry => entry.GetProperty("opcode").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(unsupportedEntries.Length, unsupported.Count);
        Assert.All(unsupportedEntries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(
                entry.GetProperty("category").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(
                entry.GetProperty("reason").GetString()));
        });

        var runtimeDefined = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => ((OpCode)field.GetValue(null)!).Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(decoded.Intersect(unsupported));
        Assert.Empty(runtimeDefined.Except(decoded.Concat(unsupported)));
        Assert.Empty(decoded.Concat(unsupported).Except(runtimeDefined));
        Assert.Equal(226, runtimeDefined.Count);
        Assert.Equal(212, decoded.Count);
        Assert.Equal(14, unsupported.Count);
    }

    private static string RepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "NetWasm.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("NetWasm repository root was not found.");
    }

    [GeneratedRegex("case \\\"([^\\\"]+)\\\"")]
    private static partial Regex DecoderCase();
}
