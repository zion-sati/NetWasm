// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Sep, the SIMD CSV reader and writer: separator detection, unescaping, parsing and writing a large file.
using System;
using System.Globalization;
using System.Text;
using nietras.SeparatedValues;

const string text = "Name;Count;Price\nApple;3;1.25\nBanana;12;0.5\n\"Quoted;Name\";1;10\n\"With \"\"quotes\"\"\";2;3.75\n";
using (var reader = Sep.Reader(options => options with { Unescape = true }).FromText(text))
{
    Console.WriteLine(reader.Spec.Sep.Separator + " " + string.Join("|", reader.Header.ColNames));
    foreach (var row in reader)
    {
        Console.WriteLine($"{row["Name"].ToString()}|{row["Count"].Parse<int>()}|{row["Price"].Parse<double>()}");
    }
}

var large = new StringBuilder("A,B,C,Text\n");
for (var index = 0; index < 20000; index++)
{
    large.Append(index).Append(',').Append(index * 0.5).Append(',').Append(-index).Append(",\"text ").Append(index % 97).Append("\"\n");
}
long sumA = 0;
double sumB = 0;
long sumC = 0;
var characters = 0;
using (var reader = Sep.Reader(options => options with { Unescape = true }).FromText(large.ToString()))
{
    foreach (var row in reader)
    {
        sumA += row["A"].Parse<long>();
        sumB += row["B"].Parse<double>();
        sumC += row[2].Parse<long>();
        characters += row["Text"].Span.Length;
    }
}
Console.WriteLine($"{sumA} {sumB} {sumC} {characters}");

using (var writer = Sep.New(',').Writer().ToText())
{
    for (var index = 0; index < 3; index++)
    {
        using var row = writer.NewRow();
        row["Name"].Set("item;" + index);
        row["Value"].Format(index * 1.5);
        row["Quoted"].Set("a,b");
    }
    Console.Write(writer.ToString());
}
var parsed = Sep.Reader(options => options with { HasHeader = false }).FromText("1\t2\t3\n4\t5\t6\n");
foreach (var row in parsed)
{
    Console.WriteLine(row.ColCount + " " + row[0].ToString() + row[2].ToString());
}
parsed.Dispose();
Console.WriteLine(double.Parse("0.1", CultureInfo.InvariantCulture));
