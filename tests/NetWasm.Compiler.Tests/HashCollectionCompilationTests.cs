// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class HashCollectionCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DictionaryEnumeratorsPreserveNonAddingMutations(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"DictionaryEnumeration_{optimize}_{target}",
            """
            using System;
            using System.Collections.Generic;

            namespace DictionaryEnumeration;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var dictionary = new Dictionary<int, string>(4);
                    dictionary.Add(1, "before");
                    dictionary.Add(2, "removed");
                    dictionary.Add(3, "after");
                    var pairs = dictionary.GetEnumerator();
                    var keys = dictionary.Keys.GetEnumerator();
                    var values = dictionary.Values.GetEnumerator();
                    dictionary[1] = "updated";
                    if (!pairs.MoveNext() || pairs.Current.Value != "updated" ||
                        !keys.MoveNext() || keys.Current != 1 ||
                        !values.MoveNext() || values.Current != "updated") return 1;
                    if (!dictionary.Remove(2, out var removed) || removed != "removed" ||
                        dictionary.Remove(99)) return 2;
                    if (!pairs.MoveNext() || pairs.Current.Key != 3 ||
                        !keys.MoveNext() || keys.Current != 3 ||
                        !values.MoveNext() || values.Current != "after") return 3;
                    dictionary.Clear();
                    dictionary.Clear();
                    if (pairs.MoveNext() || keys.MoveNext() || values.MoveNext()) return 4;
                    dictionary.Add(1, "one");
                    pairs = dictionary.GetEnumerator();
                    keys = dictionary.Keys.GetEnumerator();
                    values = dictionary.Values.GetEnumerator();
                    if (dictionary.TryAdd(1, "duplicate")) return 5;
                    if (!pairs.MoveNext() || !keys.MoveNext() || !values.MoveNext()) return 6;
                    dictionary.Add(2, "two");
                    var invalidated = 0;
                    try { pairs.MoveNext(); } catch (InvalidOperationException) { invalidated++; }
                    try { keys.MoveNext(); } catch (InvalidOperationException) { invalidated++; }
                    try { values.MoveNext(); } catch (InvalidOperationException) { invalidated++; }
                    return invalidated == 3 ? 0 : 7;
                }
            }
            """,
            "DictionaryEnumeration.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void HashSetPreservesNullElementContracts(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"HashSetNullContracts_{optimize}_{target}",
            """
            using System;
            using System.Collections.Generic;

            namespace HashSetNullContracts;

            public sealed class NullEmptyComparer : IEqualityComparer<string?>
            {
                public bool Equals(string? left, string? right) =>
                    string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right) || left == right;

                public int GetHashCode(string? value) => string.IsNullOrEmpty(value)
                    ? 0
                    : value.GetHashCode();
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var set = new HashSet<string?> { null, "one", null };
                    if (set.Count != 2 || !set.Contains(null) ||
                        !set.TryGetValue(null, out var actual) || actual is not null)
                    {
                        return 1;
                    }

                    var copy = new string?[2];
                    set.CopyTo(copy);
                    var nulls = 0;
                    foreach (var value in set)
                    {
                        if (value is null) nulls++;
                    }
                    var copiedNulls = 0;
                    foreach (var value in copy)
                    {
                        if (value is null) copiedNulls++;
                    }
                    if (nulls != 1 || copiedNulls != 1) return 2;

                    var iterator = set.GetEnumerator();
                    if (!iterator.MoveNext()) return 3;
                    if (!set.Remove(null) || set.Remove(null) || set.Contains(null)) return 4;
                    // .NET 11 permits an active HashSet enumerator to continue after removal.
                    while (iterator.MoveNext()) { }

                    set.SymmetricExceptWith(new string?[] { null, "two", null });
                    if (!set.SetEquals(new string?[] { null, "one", "two" })) return 6;
                    set.ExceptWith(new string?[] { null });
                    if (set.Contains(null) || set.Count != 2) return 7;
                    set.Clear();

                    var special = new HashSet<string?>(new NullEmptyComparer()) { "" };
                    if (special.Add(null) || !special.Contains(null) ||
                        !special.TryGetValue(null, out var empty) || empty != "" ||
                        !special.Remove(null) || special.Count != 0)
                        return 8;
                    special.Add(null);
                    if (!special.Contains("") ||
                        !special.TryGetValue("", out var nullValue) || nullValue is not null ||
                        !special.Remove("") || special.Count != 0)
                        return 9;

                    return set.Count + input - input;
                }
            }
            """,
            "HashSetNullContracts.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    [Fact]
    public void CompilerExecutesDictionaryAndHashSetCoreContracts()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "HashCollectionFixture",
            """
            using System.Collections.Generic;

            namespace HashCollectionFixture;

            public sealed class ConstantHashComparer : IEqualityComparer<int>
            {
                public bool Equals(int left, int right) => left == right;
                public int GetHashCode(int value) => 1;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var result = 0;
                    var dictionary = new Dictionary<int, string>(1, new ConstantHashComparer());
                    for (var index = 0; index < 24; index++)
                        dictionary.Add(index, index == 7 ? "seven" : "value");
                    var countMatches = dictionary.Count == 24;
                    if (input == 11) return countMatches ? 11 : -11;
                    var indexedMatches = dictionary[7] == "seven";
                    if (input == 12) return indexedMatches ? 12 : -12;
                    var found = dictionary.TryGetValue(23, out var value);
                    if (input == 13) return found ? 13 : -13;
                    var valueMatches = value == "value";
                    if (input == 14) return valueMatches ? 14 : -14;
                    if (countMatches && indexedMatches && found && valueMatches) result |= 1;
                    if (input == 1) return result;
                    if (!dictionary.TryAdd(7, "duplicate") && dictionary.Remove(3) &&
                        !dictionary.ContainsKey(3) && dictionary.TryAdd(27, "new")) result |= 2;
                    if (input == 2) return result;
                    var enumerated = 0;
                    foreach (var pair in dictionary) enumerated += pair.Key;
                    if (enumerated == 300) result |= 4;
                    if (input == 3) return result;
                    var set = new HashSet<int>(new ConstantHashComparer());
                    set.UnionWith(new[] { 1, 2, 2, 3 });
                    set.SymmetricExceptWith(new[] { 3, 4, 4 });
                    if (set.Count == 3 && set.Contains(1) && set.Contains(2) && set.Contains(4) &&
                        set.IsSupersetOf(new[] { 1, 4 }) && set.Overlaps(new[] { 0, 2 })) result |= 8;
                    var caught = false;
                    var iterator = dictionary.GetEnumerator();
                    iterator.MoveNext();
                    dictionary.Add(99, "changed");
                    try { iterator.MoveNext(); }
                    catch (System.InvalidOperationException) { caught = true; }
                    if (caught) result |= 16;
                    return result + input - input;
                }
            }
            """);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "HashCollectionFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(11, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 11));
        Assert.Equal(12, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 12));
        Assert.Equal(13, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 13));
        Assert.Equal(14, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 14));
        Assert.Equal(1, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 1));
        Assert.Equal(3, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 2));
        Assert.Equal(7, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 3));
        Assert.Equal(31, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 0));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DictionaryLookupRefsPreservePresenceAndInteriorValues(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"DictionaryLookupRefs_{optimize}_{target}",
            """
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            namespace DictionaryLookupRefs;

            public sealed class Payload
            {
                public Payload(int value) => Value = value;
                public int Value { get; }
            }

            public struct Envelope
            {
                public Envelope(Payload payload, long tag)
                {
                    Payload = payload;
                    Tag = tag;
                }

                public Payload Payload;
                public long Tag;
            }

            public sealed class CollisionComparer : IEqualityComparer<string>
            {
                public bool Equals(string? left, string? right) => left == right;
                public int GetHashCode(string value) => 1;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var nullable = new Dictionary<string, Payload?>();
                    if (nullable.ContainsKey("missing") ||
                        nullable.TryGetValue("missing", out var missing) || missing is not null)
                        return 1;
                    ref var missingRef = ref CollectionsMarshal.GetValueRefOrNullRef(
                        nullable,
                        "missing");
                    if (!Unsafe.IsNullRef(ref missingRef)) return 2;

                    ref var addedRef = ref CollectionsMarshal.GetValueRefOrAddDefault(
                        nullable,
                        "added",
                        out var addedExists);
                    if (addedExists || Unsafe.IsNullRef(ref addedRef) || addedRef is not null)
                        return 12;
                    addedRef = new Payload(19);
                    GC.Collect();
                    if (nullable["added"]?.Value != 19) return 13;
                    ref var existingAddedRef = ref CollectionsMarshal.GetValueRefOrAddDefault(
                        nullable,
                        new string("added".ToCharArray()),
                        out addedExists);
                    if (!addedExists || Unsafe.IsNullRef(ref existingAddedRef) ||
                        existingAddedRef?.Value != 19)
                        return 14;
                    existingAddedRef = new Payload(23);
                    if (nullable["added"]?.Value != 23) return 15;

                    nullable.Add("null", null);
                    if (!nullable.ContainsKey("null") ||
                        !nullable.TryGetValue("null", out var present) || present is not null ||
                        nullable["null"] is not null)
                        return 3;
                    ref var presentRef = ref CollectionsMarshal.GetValueRefOrNullRef(
                        nullable,
                        "null");
                    if (Unsafe.IsNullRef(ref presentRef)) return 4;
                    presentRef = new Payload(17);
                    if (nullable["null"]?.Value != 17) return 5;

                    var values = new Dictionary<string, Envelope>(1, new CollisionComparer());
                    for (var index = 0; index < 40; index++)
                        values.Add("key-" + index.ToString(),
                            new Envelope(new Payload(index), 0x1_0000_0000L + index));
                    var equalButDistinct = new string("key-23".ToCharArray());
                    if (!values.ContainsKey(equalButDistinct) ||
                        !values.TryGetValue(equalButDistinct, out var found) ||
                        found.Payload.Value != 23 || found.Tag != 0x1_0000_0017L)
                        return 6;

                    ref var foundRef = ref CollectionsMarshal.GetValueRefOrNullRef(
                        values,
                        equalButDistinct);
                    if (Unsafe.IsNullRef(ref foundRef)) return 7;
                    foundRef.Payload = new Payload(123);
                    foundRef.Tag = 456;
                    if (values["key-23"].Payload.Value != 123 || values["key-23"].Tag != 456)
                        return 8;

                    values["key-23"] = new Envelope(new Payload(321), 654);
                    if (values["key-23"].Payload.Value != 321 || values["key-23"].Tag != 654)
                        return 9;
                    if (!values.Remove("key-3"))
                        return 10;
                    ref var replacement = ref CollectionsMarshal.GetValueRefOrAddDefault(
                        values,
                        "replacement",
                        out var replacementExists);
                    if (replacementExists || Unsafe.IsNullRef(ref replacement)) return 16;
                    replacement = new Envelope(new Payload(3), 33);
                    if (values["replacement"].Payload.Value != 3 ||
                        values["replacement"].Tag != 33)
                        return 17;

                    try
                    {
                        _ = values.ContainsKey(null!);
                        return 11;
                    }
                    catch (ArgumentNullException)
                    {
                    }

                    return input - input;
                }
            }
            """,
            "DictionaryLookupRefs.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(0, result);
    }

    [Fact]
    public void OrdinalStringDictionarySupportsDistinctAndAlternateKeys()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "OrdinalStringDictionaryFixture",
            """
            using System;
            using System.Collections.Generic;

            namespace OrdinalStringDictionaryFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var ordinal = new Dictionary<string, int>(1, StringComparer.Ordinal);
                    for (var index = 0; index < 32; index++)
                        ordinal.Add("key-" + index.ToString(), index);
                    ordinal.Add("a\0b", 101);

                    var equalButDistinct = new string("key-17".ToCharArray());
                    if (!ordinal.TryGetValue(equalButDistinct, out var value) || value != 17)
                        return 1;
                    if (!ordinal.Remove(equalButDistinct) || ordinal.ContainsKey("key-17"))
                        return 2;
                    if (!ordinal.TryAdd("replacement", 117) || ordinal["replacement"] != 117)
                        return 3;

                    var ordinalAlternate = ordinal.GetAlternateLookup<ReadOnlySpan<char>>();
                    if (!ordinalAlternate.TryGetValue("a\0b".AsSpan(), out value) || value != 101)
                        return 4;

                    var ignoreCase = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["café-ω"] = 211,
                    };
                    if (!ignoreCase.TryGetValue("CAFÉ-Ω", out value) || value != 211)
                        return 5;
                    var ignoreCaseAlternate =
                        ignoreCase.GetAlternateLookup<ReadOnlySpan<char>>();
                    if (!ignoreCaseAlternate.TryGetValue("CAFÉ-Ω".AsSpan(), out value) ||
                        value != 211)
                        return 6;

                    return input - input;
                }
            }
            """);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "OrdinalStringDictionaryFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(0, ExecuteWithNode(compilation.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void DictionaryPreservesUpstreamConstructionCapacityAndNonGenericContracts()
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "DictionaryUpstreamContracts",
            """
            using System;
            using System.Collections;
            using System.Collections.Generic;

            namespace DictionaryUpstreamContracts;

            public sealed class AbsoluteComparer : IEqualityComparer<int>
            {
                public bool Equals(int left, int right) => Math.Abs(left) == Math.Abs(right);
                public int GetHashCode(int value) => Math.Abs(value);
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var source = new Dictionary<int, string>();
                    for (var index = 0; index < 20; index++)
                        source.Add(index, "value-" + index.ToString());

                    var copy = new Dictionary<int, string>(source);
                    if (copy.Count != 20 || copy[13] != "value-13") return 1;
                    var enumerableCopy = new Dictionary<int, string>(
                        (IEnumerable<KeyValuePair<int, string>>)source);
                    if (enumerableCopy.Count != 20 || enumerableCopy[7] != "value-7") return 2;

                    var expanded = copy.EnsureCapacity(64);
                    if (expanded < 64 || copy.EnsureCapacity(0) != expanded) return 3;
                    for (var index = 0; index < 18; index++)
                        if (!copy.Remove(index)) return 4;
                    copy.TrimExcess();
                    var trimmed = copy.EnsureCapacity(0);
                    if (trimmed >= expanded || copy.Count != 2 || copy[19] != "value-19") return 5;
                    copy.Add(21, "value-21");
                    if (!copy.ContainsKey(21)) return 6;

                    var comparer = new AbsoluteComparer();
                    var compared = new Dictionary<int, string>(comparer) { [-2] = "two" };
                    if (!ReferenceEquals(comparer, compared.Comparer) || compared[2] != "two") return 7;

                    IDictionary untyped = new Dictionary<string, int>();
                    untyped.Add("one", 1);
                    untyped["two"] = 2;
                    if (!untyped.Contains("one") || (int)untyped["two"]! != 2) return 8;
                    var entries = new DictionaryEntry[2];
                    ((ICollection)untyped).CopyTo(entries, 0);
                    var copied = 0;
                    foreach (var entry in entries)
                        copied += (int)entry.Value!;
                    if (copied != 3) return 9;

                    var iterator = untyped.GetEnumerator();
                    try
                    {
                        _ = iterator.Entry;
                        return 10;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    if (!iterator.MoveNext() || iterator.Key is not string ||
                        iterator.Value is not int)
                        return 11;
                    iterator.Reset();
                    if (!iterator.MoveNext()) return 12;
                    untyped.Add("three", 3);
                    try
                    {
                        iterator.MoveNext();
                        return 13;
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    return input - input;
                }
            }
            """,
            "DictionaryUpstreamContracts.EntryPoint",
            true,
            WasmTarget.Wasm32,
            41,
            []));

        Assert.Equal(0, result);
    }
}
