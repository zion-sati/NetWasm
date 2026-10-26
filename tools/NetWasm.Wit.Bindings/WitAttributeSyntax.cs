namespace NetWasm.Wit.Bindings;

// A world's own function is named with one argument; an interface's with two.
internal static class WitAttributeSyntax
{
    public static string Format(string attribute, string interfaceName, string name) =>
        interfaceName.Length == 0
            ? $"[{attribute}(\"{name}\")]"
            : $"[{attribute}(\"{interfaceName}\", \"{name}\")]";
}
