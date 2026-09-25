using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusSourceNamesVerifier
{
    void Verify(ImmutableArray<string> sourceNames);
}

internal sealed partial class CorpusSourceNamesVerifier : ICorpusSourceNamesVerifier
{
    public void Verify(ImmutableArray<string> sourceNames)
    {
        if (sourceNames.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one source name is required.", nameof(sourceNames));
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in sourceNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(sourceNames));
            if (!SourceName().IsMatch(name) || !names.Add(name))
            {
                throw new ArgumentException("Source names must be distinct relative C# asset identities.", nameof(sourceNames));
            }
        }
        foreach (var name in sourceNames)
        {
            for (var slash = name.IndexOf('/'); slash >= 0; slash = name.IndexOf('/', slash + 1))
            {
                if (names.Contains(name[..slash]))
                {
                    throw new ArgumentException("A source file cannot also be a parent directory.", nameof(sourceNames));
                }
            }
        }
    }

    // Logical asset identities, not arbitrary filesystem paths. No dot segments,
    // platform separators, whitespace, rooted paths or wildcard expansion.
    [GeneratedRegex(@"\A(?:[A-Za-z0-9_](?:[A-Za-z0-9_.-]*[A-Za-z0-9_])?/)*[A-Za-z0-9_][A-Za-z0-9_.-]*\.cs(?:\.txt)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex SourceName();
}
