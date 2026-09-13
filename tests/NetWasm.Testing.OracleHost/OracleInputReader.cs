using System.Text.Json;

namespace NetWasm.Testing.OracleHost;

internal interface IOracleInputReader
{
    OracleInputs? Read(string argument);
}

internal sealed class OracleInputReader : IOracleInputReader
{
    public OracleInputs? Read(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (int.TryParse(argument, out var input))
        {
            return new([input], IsBatched: false);
        }

        if (!argument.StartsWith('@'))
        {
            return null;
        }

        var path = argument[1..];
        var inputs = JsonSerializer.Deserialize<int[]>(File.ReadAllText(path));
        return inputs is { Length: > 0 }
            ? new(inputs, IsBatched: true)
            : null;
    }
}
