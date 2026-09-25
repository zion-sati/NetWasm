using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.GeneratorChecks.Contracts;

namespace NetWasm.GeneratorChecks.Activation;

public static class EntryPoint
{
    public static void Main(string[] args)
    {
        foreach (var argument in args)
            Console.WriteLine(Run(int.Parse(argument, System.Globalization.CultureInfo.InvariantCulture)));
    }

    public static int Run(int input) => input switch
    {
        0 => Lifetimes(),
        1 => Keyed(),
        2 => GenericAcrossAssembly(),
        3 => MissingService(),
        4 => BindingCreate(),
        5 => BindingUpdate(),
        6 => BindingFailure(),
        _ => throw new ArgumentOutOfRangeException(nameof(input)),
    };

    private static int Lifetimes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Singleton>();
        services.AddScoped<Scoped>();
        services.AddTransient<Consumer>();
        using var provider = services.BuildServiceProvider();
        Scoped first;
        using (var scope = provider.CreateScope())
        {
            var one = scope.ServiceProvider.GetRequiredService<Consumer>();
            var two = scope.ServiceProvider.GetRequiredService<Consumer>();
            if (ReferenceEquals(one, two) || !ReferenceEquals(one.Singleton, two.Singleton) ||
                !ReferenceEquals(one.Scoped, two.Scoped)) return -1;
            first = one.Scoped;
        }
        using var other = provider.CreateScope();
        return first.Disposed && !ReferenceEquals(first, other.ServiceProvider.GetRequiredService<Scoped>()) ? 42 : -2;
    }

    private static int Keyed()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<Singleton>("blue");
        services.AddKeyedSingleton<Singleton>("red");
        using var provider = services.BuildServiceProvider();
        var blue = provider.GetRequiredKeyedService<Singleton>("blue");
        return ReferenceEquals(blue, provider.GetRequiredKeyedService<Singleton>("blue")) &&
            !ReferenceEquals(blue, provider.GetRequiredKeyedService<Singleton>("red")) ? 42 : -3;
    }

    private static int GenericAcrossAssembly()
    {
        var services = new ServiceCollection();
        services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IRepository<int>>().Echo(42) == 42 &&
            provider.GetRequiredService<IRepository<string>>().Echo("closed") == "closed" ? 42 : -4;
    }

    private static int MissingService()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        return provider.GetService<Singleton>() is null ? 42 : -5;
    }

    private static IConfiguration Configuration(string count) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Name"] = "generated", ["Count"] = count,
            ["Child:Enabled"] = "true", ["Tags:0"] = "one", ["Tags:1"] = "two" }).Build();

    private static int BindingCreate()
    {
        var settings = Configuration("7").Get<Settings>();
        return settings is { Name: "generated", Count: 7, Child.Enabled: true } &&
            settings.Tags.Length == 2 && settings.Tags[1] == "two" ? 42 : -6;
    }

    private static int BindingUpdate()
    {
        var settings = new Settings { Name = "before", Count = null };
        Configuration("9").Bind(settings);
        return settings.Name == "generated" && settings.Count == 9 && settings.Child.Enabled &&
            settings.Tags.Length == 2 ? 42 : -7;
    }

    private static int BindingFailure()
    {
        try { _ = Configuration("not-a-number").Get<Settings>(); }
        catch (FormatException) { return 42; }
        return -8;
    }
}

public sealed class Singleton;
public sealed class Scoped : IDisposable
{
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}
public sealed class Consumer(Singleton singleton, Scoped scoped)
{
    public Singleton Singleton { get; } = singleton;
    public Scoped Scoped { get; } = scoped;
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
