namespace NetWasm.Compiler.Tests.Correctness;

internal interface ICorpusProfileOverrideParser
{
    CorpusMatrixProfile? Parse(string? value);
}

internal sealed class CorpusProfileOverrideParser : ICorpusProfileOverrideParser
{
    public CorpusMatrixProfile? Parse(string? value)
    {
        if (value is null)
        {
            return null;
        }
        if (!Enum.TryParse<CorpusMatrixProfile>(value, ignoreCase: true, out var profile) ||
            !string.Equals(Enum.GetName(profile), value, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Corpus profile must be Fast, Family or Extended.", nameof(value));
        }
        return profile;
    }
}
