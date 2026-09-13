namespace NetWasm.Wit.Bindings;

public sealed class WitBindingException(string message) : Exception(message)
{
    internal static WitBindingException Invalid(string message) => new(message);
}
