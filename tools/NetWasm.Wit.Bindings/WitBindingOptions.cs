namespace NetWasm.Wit.Bindings;

internal enum WitBindingAccessibility
{
    Public,
    Internal,
}

// `--wit -` reads WIT from standard input and `--output -` writes the source
// to standard output.
internal sealed record WitBindingOptions(
    string Wit,
    string? World,
    string Output,
    WitBindingAccessibility Accessibility = WitBindingAccessibility.Public,
    string? Namespace = null);

internal interface IWitBindingOptionsReader
{
    WitBindingOptions Read(string[] arguments);
}

internal sealed class WitBindingOptionsReader : IWitBindingOptionsReader
{
    public WitBindingOptions Read(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string? wit = null;
        string? world = null;
        string? output = null;
        string? @namespace = null;
        var accessibility = WitBindingAccessibility.Public;
        var accessibilitySpecified = false;

        for (var index = 0; index < arguments.Length; index += 2)
        {
            if (index + 1 >= arguments.Length)
            {
                throw WitBindingException.Invalid(
                    $"missing value for option '{arguments[index]}'");
            }

            var option = arguments[index];
            var value = arguments[index + 1];
            switch (option)
            {
                case "--wit":
                    wit = SetOnce(wit, value, option);
                    break;
                case "--world":
                    world = SetOnce(world, value, option);
                    break;
                case "--output":
                    output = SetOnce(output, value, option);
                    break;
                case "--namespace":
                    @namespace = SetOnce(@namespace, value, option);
                    if (!IsNamespace(@namespace))
                    {
                        throw WitBindingException.Invalid(
                            $"'{value}' is not a C# namespace");
                    }
                    break;
                case "--accessibility":
                    if (accessibilitySpecified)
                    {
                        throw WitBindingException.Invalid(
                            $"option '{option}' was specified more than once");
                    }

                    accessibility = value switch
                    {
                        "public" => WitBindingAccessibility.Public,
                        "internal" => WitBindingAccessibility.Internal,
                        _ => throw WitBindingException.Invalid(
                            $"unsupported binding accessibility '{value}'"),
                    };
                    accessibilitySpecified = true;
                    break;
                default:
                    throw WitBindingException.Invalid(
                        $"unknown wit-bindgen option '{option}'");
            }
        }

        return new WitBindingOptions(
            wit ?? throw WitBindingException.Invalid(
                "missing required option '--wit'"),
            world,
            output ?? throw WitBindingException.Invalid(
                "missing required option '--output'"),
            accessibility,
            @namespace);
    }

    // Dot-separated identifiers, without the verbatim prefix.
    private static bool IsNamespace(string value) =>
        value.Split('.').All(static part =>
            part.Length != 0 &&
            (char.IsLetter(part[0]) || part[0] == '_') &&
            part.All(static character => char.IsLetterOrDigit(character) || character == '_'));

    private static string SetOnce(string? current, string value, string option) =>
        current is null
            ? value
            : throw WitBindingException.Invalid(
                $"option '{option}' was specified more than once");
}
