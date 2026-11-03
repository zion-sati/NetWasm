namespace NetWasm.Wit.Bindings;

// The summary --help prints and an empty command line shows.
internal static class WitBindingUsage
{
    internal static readonly string Text = string.Join(Environment.NewLine,
    [
        "Usage: netwasm-wit-bindgen --wit <path> --output <path> [options]",
        "",
        "Generates reflection-free C# bindings for a WIT world.",
        "",
        "Options:",
        "  --wit <path>             The WIT file, or the WIT package directory with the",
        "                           packages it imports in a deps directory beside it.",
        "                           - reads WIT from standard input.",
        "  --output <path>          The C# file to write. - writes to standard output.",
        "  --world <name>           The world to generate, such as",
        "                           example:service@1.0.0/service. It may be left out",
        "                           when the WIT holds exactly one world.",
        "  --namespace <name>       The C# namespace of the bindings. The default is",
        "                           NetWasm.Wit followed by the world's package name.",
        "  --accessibility <value>  public (the default) or internal.",
        "  -h, --help               Show this help.",
        "",
        "Example:",
        "  netwasm-wit-bindgen --wit wit --world example:service@1.0.0/service --output Bindings.g.cs",
        "",
        "Documentation: https://github.com/zion-sati/NetWasm/blob/main/docs/wit-bindings.md",
        "",
    ]);

    internal static bool IsRequested(IReadOnlyList<string> arguments) =>
        arguments.Any(static argument => argument is "--help" or "-h" or "-?");
}
