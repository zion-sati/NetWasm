namespace NetWasm.Wit.Bindings;

internal interface IWitBindingSourceAccessibilityRewriter
{
    string Rewrite(string source, WitBindingAccessibility accessibility);
}

internal sealed class WitBindingSourceAccessibilityRewriter :
    IWitBindingSourceAccessibilityRewriter
{
    public string Rewrite(string source, WitBindingAccessibility accessibility)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (accessibility == WitBindingAccessibility.Public)
        {
            return source;
        }

        if (accessibility != WitBindingAccessibility.Internal)
        {
            throw new ArgumentOutOfRangeException(nameof(accessibility));
        }

        var rewritten = source.Replace(
            "\npublic ",
            "\ninternal ",
            StringComparison.Ordinal);
        return rewritten.StartsWith("public ", StringComparison.Ordinal)
            ? "internal " + rewritten[7..]
            : rewritten;
    }
}
