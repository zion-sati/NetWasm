using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ConcurrentDictionaryCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesSingleReactorConcurrentDictionaryContract(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ConcurrentDictionaryFixture",
            """
            namespace ConcurrentDictionaryFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int passed = 0;
                    var values = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
                    if (values.IsEmpty && values.TryAdd("one", 1) &&
                        !values.TryAdd("one", 2) && values["one"] == 1 &&
                        values.Count == 1) passed |= 1;

                    if (values.TryUpdate("one", 2, 1) &&
                        !values.TryUpdate("one", 3, 1) && values["one"] == 2 &&
                        values.GetOrAdd("one", 4) == 2 &&
                        values.AddOrUpdate("one", 5, (key, old) => old + 1) == 3)
                        passed |= 2;

                    var getReentry = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
                    int getResult = getReentry.GetOrAdd("x", key =>
                    {
                        getReentry["x"] = 10;
                        return 20;
                    });
                    if (getResult == 10 && getReentry["x"] == 10) passed |= 4;

                    var addReentry = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
                    int addCalls = 0;
                    int updateCalls = 0;
                    int addResult = addReentry.AddOrUpdate(
                        "x",
                        key =>
                        {
                            addCalls++;
                            addReentry["x"] = 10;
                            return 20;
                        },
                        (key, old) =>
                        {
                            updateCalls++;
                            return old + 1;
                        });
                    if (addResult == 11 && addReentry["x"] == 11 &&
                        addCalls == 1 && updateCalls == 1) passed |= 8;

                    var updateReentry = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
                    updateReentry["x"] = 1;
                    int reentrantUpdates = 0;
                    int updateResult = updateReentry.AddOrUpdate(
                        "x",
                        key => 0,
                        (key, old) =>
                        {
                            reentrantUpdates++;
                            if (reentrantUpdates == 1) updateReentry["x"] = 5;
                            return old + 1;
                        });
                    if (updateResult == 6 && updateReentry["x"] == 6 &&
                        reentrantUpdates == 2) passed |= 16;

                    var comparerValues =
                        new System.Collections.Concurrent.ConcurrentDictionary<string, int>(
                            System.StringComparer.OrdinalIgnoreCase);
                    comparerValues["Name"] = 7;
                    if (comparerValues.ContainsKey("name") &&
                        comparerValues.TryRemove("NAME", out int removed) && removed == 7)
                        passed |= 32;

                    var source = new System.Collections.Generic.KeyValuePair<string, int>[]
                    {
                        new("a", 1),
                        new("b", 2),
                    };
                    var copied =
                        new System.Collections.Concurrent.ConcurrentDictionary<string, int>(
                            1,
                            source,
                            System.StringComparer.Ordinal);
                    var keys = copied.Keys;
                    var storedValues = copied.Values;
                    copied["c"] = 3;
                    if (keys.Count == 2 && storedValues.Count == 2 && copied.Count == 3)
                        passed |= 64;

                    var pairs =
                        (System.Collections.Generic.ICollection<
                            System.Collections.Generic.KeyValuePair<string, int>>)copied;
                    if (!pairs.Remove(new("a", 9)) &&
                        pairs.Remove(new("a", 1)) && !copied.ContainsKey("a"))
                        passed |= 128;

                    System.Collections.IDictionary raw =
                        new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
                    raw.Add("a", 1);
                    if ((int)raw["a"]! == 1 && raw[1] == null &&
                        !raw.Contains(1) && !raw.IsFixedSize && !raw.IsReadOnly &&
                        !((System.Collections.ICollection)raw).IsSynchronized)
                        passed |= 256;

                    bool duplicateRejected = false;
                    bool badKeyRejected = false;
                    bool badValueRejected = false;
                    try { raw.Add("a", 2); }
                    catch (System.ArgumentException) { duplicateRejected = true; }
                    try { raw.Add(1, 2); }
                    catch (System.ArgumentException) { badKeyRejected = true; }
                    try { raw.Add("z", null); }
                    catch (System.ArgumentException) { badValueRejected = true; }
                    if (duplicateRejected && badKeyRejected && badValueRejected)
                        passed |= 512;

                    var array = copied.ToArray();
                    var pairCopy = new System.Collections.Generic.KeyValuePair<string, int>[2];
                    ((System.Collections.ICollection)copied).CopyTo(pairCopy, 0);
                    var entryCopy = new System.Collections.DictionaryEntry[2];
                    ((System.Collections.ICollection)copied).CopyTo(entryCopy, 0);
                    if (array.Length == 2 && pairCopy.Length == 2 &&
                        entryCopy.Length == 2) passed |= 1024;

                    bool badConcurrency = false;
                    bool badCapacity = false;
                    try
                    {
                        _ = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(0, 1);
                    }
                    catch (System.ArgumentOutOfRangeException) { badConcurrency = true; }
                    try
                    {
                        _ = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(1, -1);
                    }
                    catch (System.ArgumentOutOfRangeException) { badCapacity = true; }
                    if (badConcurrency && badCapacity) passed |= 2048;

                    bool nullGetFactory = false;
                    bool nullAddFactory = false;
                    try
                    {
                        values.GetOrAdd("one", (System.Func<string, int>)null!);
                    }
                    catch (System.ArgumentNullException) { nullGetFactory = true; }
                    try
                    {
                        values.AddOrUpdate(
                            "one",
                            (System.Func<string, int>)null!,
                            (key, old) => old);
                    }
                    catch (System.ArgumentNullException) { nullAddFactory = true; }
                    if (nullGetFactory && nullAddFactory) passed |= 4096;

                    return passed;
                }
            }
            """,
            "10.0.403",
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConcurrentDictionaryFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(
            8191,
            ExecuteWithStandardWasiNode(
                compilation.ApplicationModule,
                assets.Directory,
                0,
                target,
                compilation.StaticDataEnd));
    }
}
