// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// MemoryPack's source-generated serializers: classes, records, structs, unions, version-tolerant types and
// collections, compared byte for byte.
using System;
using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using MemoryPack.Formatters;

// Each type registers its generated formatter up front, as MemoryPack's ahead-of-time path does. Otherwise MemoryPack
// finds a type's RegisterFormatter by reflection on first use, and creates an array formatter's generic type at run
// time, which a NetWasm program, compiled ahead of time without reflection metadata, cannot.
MemoryPackFormatterProvider.Register<Person>();
MemoryPackFormatterProvider.Register<Address>();
MemoryPackFormatterProvider.Register<Point>();
MemoryPackFormatterProvider.Register<IShape>();
MemoryPackFormatterProvider.Register<Circle>();
MemoryPackFormatterProvider.Register<Square>();
MemoryPackFormatterProvider.Register<VersionOne>();
MemoryPackFormatterProvider.Register<VersionTwo>();
MemoryPackFormatterProvider.Register(new ListFormatter<int>());

var person = new Person
{
    Id = 7, Name = "Ada", Scores = [1.5, 2.25, -0.0], Tags = new() { ["x"] = 1, ["y"] = 2 },
    Born = new DateTime(1815, 12, 10, 0, 0, 0, DateTimeKind.Utc), Address = new Address("1 Way", 12345), Optional = null,
    Kind = Kind.Admin, Key = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"), Money = 12.345m,
    Children = [new Person { Id = 8, Name = "Kid", Kind = Kind.User }],
};
var bytes = MemoryPackSerializer.Serialize(person);
Console.WriteLine(Convert.ToHexString(bytes));
var back = MemoryPackSerializer.Deserialize<Person>(bytes)!;
Console.WriteLine($"{back.Id} {back.Name} {string.Join(",", back.Scores)} {string.Join(",", back.Tags.Select(pair => pair.Key + pair.Value))}");
Console.WriteLine($"{back.Born:O} {back.Address} {back.Optional?.ToString() ?? "null"} {back.Kind} {back.Key} {back.Money} {back.Children[0].Name}");

IShape[] shapes = [new Circle { Radius = 2 }, new Square { Side = 3 }];
var shapeBytes = MemoryPackSerializer.Serialize(shapes);
Console.WriteLine(Convert.ToHexString(shapeBytes));
foreach (var shape in MemoryPackSerializer.Deserialize<IShape[]>(shapeBytes)!)
{
    Console.WriteLine($"{shape.GetType().Name} {shape.Area():F3}");
}

var point = new Point(3, -4);
var pointBytes = MemoryPackSerializer.Serialize(point);
Console.WriteLine(Convert.ToHexString(pointBytes) + " " + MemoryPackSerializer.Deserialize<Point>(pointBytes));
var tolerant = MemoryPackSerializer.Serialize(new VersionOne { A = 1, B = "two" });
var upgraded = MemoryPackSerializer.Deserialize<VersionTwo>(tolerant)!;
Console.WriteLine($"{Convert.ToHexString(tolerant)} {upgraded.A} {upgraded.B} {upgraded.C}");
var list = Enumerable.Range(0, 1000).Select(index => index * 31).ToList();
var listBytes = MemoryPackSerializer.Serialize(list);
Console.WriteLine($"{listBytes.Length} {MemoryPackSerializer.Deserialize<List<int>>(listBytes)!.Sum()}");
try
{
    MemoryPackSerializer.Deserialize<Person>(bytes.AsSpan(0, 10));
}
catch (Exception error)
{
    Console.WriteLine(error.GetType().Name);
}

public enum Kind : byte { User, Admin }

[MemoryPackable]
public partial class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double[] Scores { get; set; } = [];
    public Dictionary<string, int> Tags { get; set; } = [];
    public DateTime Born { get; set; }
    public Address? Address { get; set; }
    public int? Optional { get; set; }
    public Kind Kind { get; set; }
    public Guid Key { get; set; }
    public decimal Money { get; set; }
    public List<Person> Children { get; set; } = [];
}

[MemoryPackable]
public partial record Address(string Street, int Zip);

[MemoryPackable]
public partial struct Point(int x, int y)
{
    public int X { get; set; } = x;
    public int Y { get; set; } = y;

    public override readonly string ToString() => $"({X},{Y})";
}

[MemoryPackable]
[MemoryPackUnion(0, typeof(Circle))]
[MemoryPackUnion(1, typeof(Square))]
public partial interface IShape
{
    double Area();
}

[MemoryPackable]
public partial class Circle : IShape
{
    public double Radius { get; set; }

    public double Area() => Math.PI * Radius * Radius;
}

[MemoryPackable]
public partial class Square : IShape
{
    public double Side { get; set; }

    public double Area() => Side * Side;
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class VersionOne
{
    [MemoryPackOrder(0)] public int A { get; set; }
    [MemoryPackOrder(1)] public string B { get; set; } = "";
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class VersionTwo
{
    [MemoryPackOrder(0)] public int A { get; set; }
    [MemoryPackOrder(1)] public string B { get; set; } = "";
    [MemoryPackOrder(2)] public int C { get; set; } = 99;
}
