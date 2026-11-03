namespace NetWasm.Wit.Bindings;

public sealed class WitBindingException(string message, bool usage = false) : Exception(message)
{
    // An error in the command line itself, which the usage summary explains.
    internal bool IsUsage { get; } = usage;

    internal static WitBindingException Invalid(string message) => new(message);

    internal static WitBindingException Usage(string message) => new(message, usage: true);
}
