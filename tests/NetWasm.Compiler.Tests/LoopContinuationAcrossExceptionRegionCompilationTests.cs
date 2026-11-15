// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Loops whose bodies hold nested exception regions, in the shapes of CookieContainer's
// cleanup and aging. Optimized code leaves a protected region straight to the loop's
// continuation, which once produced a loop continue inside a nested region.
public sealed class LoopContinuationAcrossExceptionRegionCompilationTests
{
    public static TheoryData<string, bool, WasmTarget> Cases()
    {
        var cases = new TheoryData<string, bool, WasmTarget>();
        foreach (var shape in new[] { "ContinueOutOfLock", "ContinueInsideTryCatchDoWhile", "AgeShape", "CleanupShape" })
        {
            foreach (var optimize in new[] { false, true })
            {
                foreach (var target in new[] { WasmTarget.Wasm32, WasmTarget.Wasm64 })
                {
                    cases.Add(shape, optimize, target);
                }
            }
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void LoopsAroundNestedProtectedRegionsCompileAndRun(string shape, bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var source = shape switch
        {
            "ContinueOutOfLock" => LockSource,
            "AgeShape" => AgeSource,
            "CleanupShape" => CleanupSource,
            _ => TrySource,
        };
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"LoopProbe{shape}{(optimize ? "Release" : "Debug")}{target}",
            source,
            "LoopProbe.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(42, observed);
    }

    private const string LockSource = """
        using System.Collections.Generic;
        namespace LoopProbe;
        public static class EntryPoint
        {
            static readonly object Gate = new();
            public static int Run(int input)
            {
                var keys = new List<string> { "a", "missing", "b" };
                var map = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
                int sum = 0;
                for (int i = 0; i < keys.Count; i++)
                {
                    int value;
                    lock (Gate)
                    {
                        if (!map.TryGetValue(keys[i], out value))
                        {
                            continue;
                        }
                    }
                    sum += value;
                }
                return sum == 3 ? input + 1 : 1;
            }
        }
        """;

    private const string TrySource = """
        using System;
        namespace LoopProbe;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                int total = 0, i = 0;
                try
                {
                    do
                    {
                        i++;
                        if (i % 2 == 0)
                        {
                            if (i > 6)
                            {
                                break;
                            }
                            continue;
                        }
                        total += i;
                    } while (true);
                }
                catch (OutOfMemoryException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException("loop", e);
                }
                return total == 16 ? input + 1 : 2;
            }
        }
        """;

    private const string AgeSource = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        namespace LoopProbe;
        public static class EntryPoint
        {
            static readonly Hashtable Table = new();
            static int count;

            static bool Age(string? domain, int maxCookies, int maxPerDomain)
            {
                int removed = 0, itemp = 0;
                float remainingFraction = count > maxCookies ? (float)maxCookies / count : 1.0F;
                List<int>? lru = null;
                int oldUsed = int.MaxValue;
                lock (Table.SyncRoot)
                {
                    foreach (object item in Table)
                    {
                        DictionaryEntry entry = (DictionaryEntry)item;
                        var pathList = domain == null ? (List<List<int>>)entry.Value! : (List<List<int>>)Table[domain]!;
                        int domainCount = 0;
                        lock (pathList)
                        {
                            foreach (List<int> cc in pathList)
                            {
                                domainCount += cc.Count;
                                if (cc.Count > 0 && cc[0] < oldUsed)
                                {
                                    lru = cc;
                                    oldUsed = cc[0];
                                }
                            }
                        }
                        int minCount = Math.Min((int)(domainCount * remainingFraction), Math.Min(maxPerDomain, maxCookies) - 1);
                        if (domainCount > minCount)
                        {
                            List<int>[] lists;
                            lock (pathList)
                            {
                                lists = pathList.ToArray();
                            }
                            itemp = 0;
                            for (int i = 0; i < lists.Length; ++i)
                            {
                                var cc = lists[i];
                                lock (cc)
                                {
                                    while (domainCount > minCount && cc.Count > 0)
                                    {
                                        cc.RemoveAt(0);
                                        --domainCount;
                                        --count;
                                        ++removed;
                                    }
                                }
                                if (domainCount <= minCount)
                                {
                                    break;
                                }
                            }
                            if (domainCount > minCount && domain != null)
                            {
                                return false;
                            }
                        }
                    }
                }
                if (domain != null) return true;
                if (removed != 0) return true;
                if (oldUsed == int.MaxValue) return false;
                lock (lru!)
                {
                    while (count >= maxCookies && lru.Count > 0)
                    {
                        lru.RemoveAt(0);
                        --count;
                    }
                }
                return true;
            }

            public static int Run(int input)
            {
                Table["a"] = new List<List<int>> { new List<int> { 1, 2, 3 }, new List<int> { 4 } };
                Table["b"] = new List<List<int>> { new List<int> { 5, 6 } };
                count = 6;
                return Age(null, 3, 2) && count < 6 ? input + 1 : 1;
            }
        }
        """;

    private const string CleanupSource = """
        using System.Collections;
        using System.Collections.Generic;
        namespace LoopProbe;
        public static class EntryPoint
        {
            static readonly Hashtable Table = new();

            static void Cleanup()
            {
                var removePathList = new List<object>();
                var removeDomainList = new List<string>();
                lock (Table.SyncRoot)
                {
                    IDictionaryEnumerator enumerator = Table.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        string currentDomain = (string)enumerator.Key;
                        var pathList = (Hashtable)enumerator.Value!;
                        lock (pathList.SyncRoot)
                        {
                            IDictionaryEnumerator e = pathList.GetEnumerator();
                            while (e.MoveNext())
                            {
                                if ((int)e.Value! == 0)
                                {
                                    removePathList.Add(e.Key);
                                }
                            }
                            foreach (var key in removePathList)
                            {
                                pathList.Remove(key);
                            }
                            removePathList.Clear();
                            if (pathList.Count == 0) removeDomainList.Add(currentDomain);
                        }
                    }
                    foreach (var key in removeDomainList)
                    {
                        Table.Remove(key);
                    }
                }
            }

            public static int Run(int input)
            {
                Table["a"] = new Hashtable { ["/"] = 0 };
                Table["b"] = new Hashtable { ["/"] = 1, ["/x"] = 0 };
                Cleanup();
                return Table.Count == 1 && ((Hashtable)Table["b"]!).Count == 1 ? input + 1 : 1;
            }
        }
        """;
}
