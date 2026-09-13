namespace NetWasm.Wit.Bindings;

internal enum WitBindingAccessibility
{
    Public,
    Internal,
}

internal sealed record WitBindingOptions(
    string Wit,
    string? World,
    string Output,
    WitBindingAccessibility Accessibility = WitBindingAccessibility.Public);

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
            accessibility);
    }

    private static string SetOnce(string? current, string value, string option) =>
        current is null
            ? value
            : throw WitBindingException.Invalid(
                $"option '{option}' was specified more than once");
}
