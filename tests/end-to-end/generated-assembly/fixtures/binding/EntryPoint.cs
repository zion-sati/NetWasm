using Microsoft.Extensions.Configuration;

namespace NetWasm.GeneratorChecks.Binding;

public static class EntryPoint
{
    public static void Main(string[] args)
    {
        foreach (var argument in args)
            Console.WriteLine(Run(int.Parse(argument, System.Globalization.CultureInfo.InvariantCulture)));
    }

    public static int Run(int input)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Name"] = "generated", ["Count"] = input == 2 ? "not-a-number" : "7",
                ["Child:Enabled"] = "true", ["Tags:0"] = "one", ["Tags:1"] = "two",
            }).Build();
        if (input == 2)
        {
            try { _ = configuration.Get<Settings>(); }
            catch (FormatException) { return 42; }
            return -1;
        }
        if (input is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(input));
        var settings = new Settings { Name = "before", Count = null };
        if (input == 0) settings = configuration.Get<Settings>()!;
        else configuration.Bind(settings);
        return settings is { Name: "generated", Count: 7, Child.Enabled: true } &&
            settings.Tags.Length == 2 && settings.Tags[0] == "one" && settings.Tags[1] == "two" ? 42 : -2;
    }
}

public sealed class Settings
{
    public string Name { get; set; } = "";
    public int? Count { get; set; }
    public Child Child { get; set; } = new();
    public string[] Tags { get; set; } = [];
}

public sealed class Child
{
    public bool Enabled { get; set; }
}
