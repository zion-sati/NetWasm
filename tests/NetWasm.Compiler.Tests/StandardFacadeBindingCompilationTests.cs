using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A library built for netstandard2.0 or netstandard2.1 names only netstandard. NetWasm.App.Ref's netstandard.dll
// forwards each standard type to its owner, CoreLib or a NetWasm.App library, so the library's calls bind across
// every owner it uses and run, as the SDK compiles a package it restores.
public sealed class StandardFacadeBindingCompilationTests
{
    [Theory]
    [InlineData("netstandard2.1", WasmTarget.Wasm32)]
    [InlineData("netstandard2.1", WasmTarget.Wasm64)]
    [InlineData("netstandard2.0", WasmTarget.Wasm32)]
    public void StandardLibraryBindsEveryOwnerThroughTheFacade(string standard, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var facade = TestAssets.StandardFacade(assets.Root);
        Assert.True(File.Exists(facade), "Build src/NetWasm.App.Ref to generate netstandard.dll.");
        var library = assets.CompileSourceAgainstCoreLib("StandardLibrary", LibrarySource, StandardContract(standard));
        var application = assets.CompileSource("StandardApplication", ApplicationSource, library, facade);
        var result = NetWasmCompiler.Compile(new(application, [assets.CoreLib, library, .. TestAssets.FrameworkAssemblies(assets.Root)],
            "StandardApplication.EntryPoint", "Run", [new("run", "StandardApplication.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform@1.0.0/platform",
            MetadataReferencePaths: [facade]));

        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    // The .NET Standard reference contracts NuGet restores (NETStandard.Library.Ref 2.1.0, NETStandard.Library 2.0.3).
    private static string StandardContract(string standard)
    {
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var contract = standard == "netstandard2.1"
            ? Path.Combine(packages, "netstandard.library.ref", "2.1.0", "ref", "netstandard2.1", "netstandard.dll")
            : Path.Combine(packages, "netstandard.library", "2.0.3", "build", "netstandard2.0", "ref", "netstandard.dll");
        Assert.True(File.Exists(contract), $"Restore {Path.GetFileName(Path.GetDirectoryName(contract))}'s contract first.");
        return contract;
    }

    // One call into each owner: CoreLib, System.Collections, System.Collections.Concurrent,
    // System.Collections.NonGeneric, System.Collections.Specialized, System.ObjectModel,
    // System.Text.RegularExpressions, System.Xml.ReaderWriter and System.Runtime.Numerics.
    private const string LibrarySource = """
        using System;
        using System.Collections;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        using System.Collections.Specialized;
        using System.Numerics;
        using System.Text;
        using System.Text.RegularExpressions;
        using System.Xml;
        namespace StandardLibrary;
        public static class Owners
        {
            public static int Check(int input)
            {
                var list = new List<int> { input };
                var stack = new Stack<int>(); stack.Push(2); stack.Push(3);
                var sorted = new SortedSet<int> { 5, 1, 3 };
                var bag = new ConcurrentBag<int> { 7 };
                var table = new SortedList { ["b"] = 2, ["a"] = 1 };
                var names = new NameValueCollection { ["key"] = "value" };
                var observed = new ObservableCollection<string>(); var changes = 0;
                observed.CollectionChanged += (_, _) => changes++;
                observed.Add("x");
                var match = Regex.Match("order-42", @"(\d+)");
                var document = new XmlDocument(); document.LoadXml("<root><item>6</item></root>");
                var power = BigInteger.Pow(2, 70) / BigInteger.Pow(2, 68);
                var text = new StringBuilder().Append(match.Groups[1].Value).ToString();
                var checks =
                    list[0] == input && stack.Pop() == 3 && sorted.Min == 1 && bag.TryTake(out var taken) && taken == 7 &&
                    (string)table.GetKey(0) == "a" && names["key"] == "value" && changes == 1 &&
                    text == "42" && document.SelectSingleNode("/root/item")!.InnerText == "6" && power == 4;
                return checks ? input + 1 : -1;
            }
        }
        """;

    private const string ApplicationSource = """
        namespace StandardApplication;
        public static class EntryPoint
        {
            public static int Run(int input) => StandardLibrary.Owners.Check(input);
        }
        """;
}
