// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Tests.StjPolymorphism.Fixture;

// Polymorphism through the ordinary SDK System.Text.Json generator: a hierarchy with discriminators, values typed
// object, and collections of either, written and read back. Each run returns a checksum of what it wrote and read.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(Dog), "dog")]
[JsonDerivedType(typeof(Cat), "cat")]
public abstract class Animal
{
    public string Name { get; set; } = "";
}

public sealed class Dog : Animal
{
    public int Barks { get; set; }
}

public sealed class Cat : Animal
{
    public bool Indoor { get; set; }
}

[JsonDerivedType(typeof(Truck))]
public class Vehicle
{
    public int Wheels { get; set; }
}

public sealed class Truck : Vehicle
{
    public double Load { get; set; }
}

public sealed class Household
{
    public List<Animal> Pets { get; set; } = [];

    public Animal? Favorite { get; set; }

    public object? Extra { get; set; }

    public Dictionary<string, object> Bag { get; set; } = [];

    public Vehicle? Car { get; set; }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Household))]
[JsonSerializable(typeof(Animal))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(List<int>))]
public partial class PolymorphismJsonContext : JsonSerializerContext;

public static class EntryPoint
{
    public static int Run(int input)
    {
        var context = PolymorphismJsonContext.Default;
        switch (input)
        {
            case 0:
            {
                var household = new Household
                {
                    Pets = [new Dog { Name = "Rex", Barks = 3 }, new Cat { Name = "Tom", Indoor = true }],
                    Favorite = new Cat { Name = "Kit" },
                    Extra = new List<int> { 1, 2 },
                    Bag = new() { ["n"] = 4, ["s"] = "x", ["b"] = false, ["d"] = 1.5 },
                    Car = new Truck { Wheels = 6, Load = 2.5 },
                };
                return Checksum(JsonSerializer.Serialize(household, context.Household));
            }
            case 1:
            {
                const string json = """{"Pets":[{"$kind":"cat","Indoor":true,"Name":"A"},{"$kind":"dog","Barks":2,"Name":"B"}],"Favorite":{"$kind":"dog","Barks":9,"Name":"C"}}""";
                var household = JsonSerializer.Deserialize(json, context.Household)!;
                var text = string.Join(",", household.Pets.Select(pet => pet switch
                {
                    Dog dog => "dog:" + dog.Name + dog.Barks,
                    Cat cat => "cat:" + cat.Name + cat.Indoor,
                    _ => "?",
                })) + "|" + (household.Favorite is Dog { Barks: 9 } ? "dog" : "other");
                return Checksum(text);
            }
            case 2:
                // A runtime type the context does not describe cannot be written through object.
                try
                {
                    JsonSerializer.Serialize(new Household { Extra = new Uri("https://example.com") }, context.Household);
                    return -1;
                }
                catch (NotSupportedException)
                {
                    return 7;
                }
            case 3:
            {
                // An unknown discriminator, and a base type written as itself.
                var text = JsonSerializer.Serialize<Animal>(new Dog { Name = "D", Barks = 1 }, context.Animal);
                try
                {
                    JsonSerializer.Deserialize("""{"$kind":"cow","Name":"E"}""", context.Animal);
                    return -2;
                }
                catch (JsonException)
                {
                    return Checksum(text + JsonSerializer.Serialize(new Household { Car = new Vehicle { Wheels = 4 } }, context.Household));
                }
            }
            default:
                return -3;
        }
    }

    private static int Checksum(string text)
    {
        var hash = 17;
        foreach (var value in Encoding.UTF8.GetBytes(text)) hash = unchecked(hash * 31 + value);
        return hash;
    }
}
