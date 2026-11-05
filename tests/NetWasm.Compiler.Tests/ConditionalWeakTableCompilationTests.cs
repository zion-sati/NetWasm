using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using System.Text;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ConditionalWeakTableCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesConditionalWeakTableApiContract(
        bool optimized,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "ConditionalWeakTableFixture",
            """
            namespace ConditionalWeakTableFixture;

            public sealed class Key
            {
                public int Id;
                public override bool Equals(object? other) => other is Key;
                public override int GetHashCode() => 1;
            }

            public sealed class Value
            {
                public int Number;
                public Key? Key;

                public Value()
                {
                    Number = 42;
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int passed = 0;
                    var table = new System.Runtime.CompilerServices.ConditionalWeakTable<Key, Value>();
                    var first = new Key { Id = 1 };
                    var firstValue = new Value { Number = 10 };
                    table.Add(first, firstValue);
                    if (table.TryGetValue(first, out var found) &&
                        object.ReferenceEquals(firstValue, found)) passed |= 1;

                    var equalButDistinct = new Key { Id = 1 };
                    if (!table.TryGetValue(equalButDistinct, out _)) passed |= 2;

                    bool duplicateRejected = false;
                    try { table.Add(first, new Value()); }
                    catch (System.ArgumentException) { duplicateRejected = true; }
                    if (duplicateRejected) passed |= 4;

                    if (table.Remove(first) && !table.Remove(first) &&
                        !table.TryGetValue(first, out _)) passed |= 8;

                    var nullKey = new Key { Id = 2 };
                    table.Add(nullKey, null!);
                    if (table.TryGetValue(nullKey, out var nullValue) && nullValue == null)
                        passed |= 16;

                    var reentryTable =
                        new System.Runtime.CompilerServices.ConditionalWeakTable<Key, Value>();
                    var reentryKey = new Key();
                    var reentryResult = reentryTable.GetValue(reentryKey, key =>
                    {
                        reentryTable.Add(key, new Value { Number = 30 });
                        return new Value { Number = 40 };
                    });
                    if (reentryResult.Number == 30 &&
                        reentryTable.TryGetValue(reentryKey, out var reentryStored) &&
                        reentryStored.Number == 30) passed |= 32;

                    var nestedTable =
                        new System.Runtime.CompilerServices.ConditionalWeakTable<Key, Value>();
                    var nestedKey = new Key();
                    int calls = 0;
                    var nestedResult = nestedTable.GetValue(nestedKey, key =>
                    {
                        calls++;
                        nestedTable.GetValue(key, _ => new Value { Number = 50 });
                        return new Value { Number = 60 };
                    });
                    if (nestedResult.Number == 50 && calls == 1) passed |= 64;

                    var defaultTable =
                        new System.Runtime.CompilerServices.ConditionalWeakTable<Key, Value>();
                    var defaultValue = defaultTable.GetOrCreateValue(new Key());
                    if (defaultValue.Number == 42) passed |= 128;

                    bool nullKeyRejected = false;
                    bool nullFactoryRejected = false;
                    try { table.TryGetValue(null!, out _); }
                    catch (System.ArgumentNullException) { nullKeyRejected = true; }
                    try
                    {
                        table.GetValue(
                            first,
                            (System.Runtime.CompilerServices.ConditionalWeakTable<
                                Key,
                                Value>.CreateValueCallback)null!);
                    }
                    catch (System.ArgumentNullException) { nullFactoryRejected = true; }
                    if (nullKeyRejected && nullFactoryRejected) passed |= 256;

                    var apiTable =
                        new System.Runtime.CompilerServices.ConditionalWeakTable<Key, Value>();
                    var apiKey = new Key();
                    var apiFirst = new Value { Number = 70 };
                    var apiSecond = new Value { Number = 71 };
                    if (apiTable.TryAdd(apiKey, apiFirst) &&
                        !apiTable.TryAdd(apiKey, apiSecond))
                    {
                        apiTable.AddOrUpdate(apiKey, apiSecond);
                        if (apiTable.Remove(apiKey, out var removed) &&
                            object.ReferenceEquals(removed, apiSecond) &&
                            !apiTable.Remove(apiKey, out _)) passed |= 512;
                    }

                    var factoryTable =
                        new System.Runtime.CompilerServices.ConditionalWeakTable<Key, Value>();
                    var directKey = new Key();
                    var direct = factoryTable.GetOrAdd(directKey, new Value { Number = 80 });
                    var callback = factoryTable.GetOrAdd(
                        new Key(),
                        static _ => new Value { Number = 81 });
                    var argument = factoryTable.GetOrAdd(
                        new Key(),
                        static (_, number) => new Value { Number = number },
                        82);
                    if (direct.Number == 80 && callback.Number == 81 && argument.Number == 82)
                        passed |= 1024;

                    var enumerationTable =
                        new System.Runtime.CompilerServices.ConditionalWeakTable<Key, Value>();
                    var enumerationKeys = new Key[17];
                    for (int index = 0; index < 8; index++)
                    {
                        enumerationKeys[index] = new Key { Id = index };
                        enumerationTable.Add(
                            enumerationKeys[index],
                            new Value { Number = index });
                    }
                    var enumerator =
                        ((System.Collections.Generic.IEnumerable<
                            System.Collections.Generic.KeyValuePair<Key, Value>>)enumerationTable)
                        .GetEnumerator();
                    enumerationKeys[8] = new Key { Id = 8 };
                    enumerationTable.Add(enumerationKeys[8], new Value { Number = 8 });
                    enumerationTable.Remove(enumerationKeys[3]);
                    int enumerated = 0;
                    while (enumerator.MoveNext()) enumerated++;
                    enumerator.Dispose();

                    for (int index = 9; index < enumerationKeys.Length; index++)
                    {
                        enumerationKeys[index] = new Key { Id = index };
                        enumerationTable.Add(
                            enumerationKeys[index],
                            new Value { Number = index });
                    }
                    bool compactedEntriesSurvived =
                        enumerationTable.TryGetValue(enumerationKeys[0], out _) &&
                        !enumerationTable.TryGetValue(enumerationKeys[3], out _) &&
                        enumerationTable.TryGetValue(enumerationKeys[16], out _);
                    enumerationTable.Clear();
                    if (enumerated == 7 && compactedEntriesSurvived &&
                        !enumerationTable.TryGetValue(enumerationKeys[0], out _)) passed |= 2048;

                    var clearTable =
                        new System.Runtime.CompilerServices.ConditionalWeakTable<Key, Value>();
                    var clearedKey = new Key();
                    clearTable.Add(clearedKey, new Value());
                    var clearEnumerator =
                        ((System.Collections.Generic.IEnumerable<
                            System.Collections.Generic.KeyValuePair<Key, Value>>)clearTable)
                        .GetEnumerator();
                    clearTable.Clear();
                    var postClearKey = new Key();
                    clearTable.Add(postClearKey, new Value { Number = 90 });
                    bool clearEnumeratorEmpty = !clearEnumerator.MoveNext();
                    clearEnumerator.Dispose();
                    if (!clearEnumeratorEmpty ||
                        !clearTable.TryGetValue(postClearKey, out var postClearValue) ||
                        postClearValue.Number != 90) return -1;

                    object dependentTarget = new();
                    object dependentValue = new();
                    var dependentHandle =
                        new System.Runtime.DependentHandle(dependentTarget, dependentValue);
                    var pair = dependentHandle.TargetAndDependent;
                    object replacementDependent = new();
                    dependentHandle.Dependent = replacementDependent;
                    bool targetSetRejected = false;
                    try { dependentHandle.Target = new object(); }
                    catch (System.InvalidOperationException) { targetSetRejected = true; }
                    if (dependentHandle.IsAllocated &&
                        object.ReferenceEquals(pair.Target, dependentTarget) &&
                        object.ReferenceEquals(pair.Dependent, dependentValue) &&
                        object.ReferenceEquals(dependentHandle.Dependent, replacementDependent) &&
                        targetSetRejected)
                    {
                        dependentHandle.Target = null;
                        if (dependentHandle.Target is null && dependentHandle.Dependent is null)
                            passed |= 4096;
                    }
                    dependentHandle.Dispose();

                    var emptyHandle = new System.Runtime.DependentHandle(null, new object());
                    bool disposedRejected = false;
                    if (emptyHandle.IsAllocated && emptyHandle.Target is null &&
                        emptyHandle.Dependent is null)
                    {
                        emptyHandle.Dispose();
                        try { _ = emptyHandle.Target; }
                        catch (System.InvalidOperationException) { disposedRejected = true; }
                    }
                    if (disposedRejected) passed |= 8192;

                    return passed;
                }
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            optimized);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ConditionalWeakTableFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        var moduleText = Encoding.UTF8.GetString(compilation.ApplicationModule);
        Assert.Contains(
            NetWasmRuntimeFeatureIds.EphemeronHandles,
            compilation.RuntimeFeatures);
        Assert.Contains("ephemeron_handle_new", moduleText, StringComparison.Ordinal);
        Assert.Contains(
            "ephemeron_handle_set_target_to_null",
            moduleText,
            StringComparison.Ordinal);
        Assert.Contains("ephemeron_handle_set_value", moduleText, StringComparison.Ordinal);
        Assert.Contains("ephemeron_handle_release", moduleText, StringComparison.Ordinal);

        Assert.Equal(
            16383,
            ExecuteWithStandardWasiNode(
                compilation.ApplicationModule,
                assets.Directory,
                0,
                target,
                compilation.StaticDataEnd));
    }

    [Fact]
    public void CompilerOmitsConditionalWeakTableRuntimeImportsWhenUnreachable()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSourceWithCompiler(
            "Hello42PayAsYouUseFixture",
            """
            namespace Hello42PayAsYouUseFixture;

            public static class EntryPoint
            {
                public static int Run(int input) => 42 + (input - input);
            }
            """,
            TestAssets.RepositorySdkVersion,
            "latest",
            false);

        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "Hello42PayAsYouUseFixture.EntryPoint",
            "Run",
            []));

        var moduleText = Encoding.UTF8.GetString(compilation.ApplicationModule);
        Assert.DoesNotContain(
            NetWasmRuntimeFeatureIds.EphemeronHandles,
            compilation.RuntimeFeatures);
        Assert.DoesNotContain("ephemeron_handle_", moduleText, StringComparison.Ordinal);
        Assert.DoesNotContain("ConditionalWeakTable", moduleText, StringComparison.Ordinal);
    }
}
