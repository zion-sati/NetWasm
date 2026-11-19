// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

// Microsoft.Extensions.Configuration's in-memory, JSON and command-line providers, layered and read back.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;

const string json = """
{
  "App": { "Name": "Demo", "Retries": 3, "Ratio": 0.25, "Tags": [ "a", "b", "c" ],
           "Nested": { "Enabled": true, "Level": "High" } },
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft": "Warning" } },
  "Escaped": "line\nbreak é \"quoted\""
}
""";

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["App:Name"] = "Base", ["Extra:Key"] = "value" })
    .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
    .AddCommandLine(["--App:Retries=5", "/Extra:Flag", "on", "-x", "short"], new Dictionary<string, string> { ["-x"] = "Extra:Short" })
    .Build();

foreach (var (key, value) in configuration.AsEnumerable().OrderBy(pair => pair.Key, StringComparer.Ordinal))
{
    Console.WriteLine($"{key} = {value}");
}
Console.WriteLine(configuration["App:Name"] + " " + configuration["app:retries"] + " " + (configuration["Missing"] ?? "null"));
Console.WriteLine(string.Join(",", configuration.GetSection("App:Tags").GetChildren().Select(child => child.Path + "=" + child.Value)));
Console.WriteLine(string.Join(";", configuration.GetSection("Logging:LogLevel").GetChildren().Select(child => child.Key + ":" + child.Value)));
Console.WriteLine($"{configuration.GetSection("App:Nested").Exists()} {configuration.GetSection("Nope").Exists()} {configuration.GetSection("App").Value ?? "no value"}");
configuration["Extra:Key"] = "changed";
Console.WriteLine(configuration["Extra:Key"]);
var root = (IConfigurationRoot)configuration;
Console.WriteLine(string.Join(",", root.Providers.Select(provider => provider.GetType().Name)));
Console.WriteLine(root.GetDebugView());
try
{
    new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("{ \"broken\": "))).Build();
}
catch (Exception error)
{
    Console.WriteLine(error.GetType().Name + ": " + error.Message);
}
