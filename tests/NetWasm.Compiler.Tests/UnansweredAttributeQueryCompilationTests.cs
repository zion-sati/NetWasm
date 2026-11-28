// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Every reached attribute query the compiler cannot answer keeps its run-time path, which throws
// PlatformNotSupportedException, and the build warns NW1019 for it with the reason: a filter known only at run time, a
// member target, a Type known only at run time with a filter that takes every attribute, an assembly known only at run
// time, an open generic target, a Type's own query with a filter that is not an attribute type, a delegate to the query,
// a method handle to it (an expression tree), and an overload taking a ParameterInfo. A query the run-time Type reducer
// answers, and one that throws what desktop throws first (a null target or filter, an interface filter for Attribute's
// statics), warn nothing. A query through an interface or a virtual method on a target the compile cannot name says it
// throws only on the runtime's own reflection objects where the program creates a class that answers it itself. A query
// in a branch the whole-program optimizations fold away (O2 and up, Os, Oz) warns only where the branch stays.
public sealed class UnansweredAttributeQueryCompilationTests
{
    private static readonly (string Method, string Reason)[] Expected =
    [
        ("RunTimeFilter", "its filter is not a Type the compile knows"),
        ("MemberTarget", "its target is a member the compile cannot prove to be a Type"),
        ("EveryAttribute", "its target is a Type known only at run time and its filter takes every attribute"),
        ("RunTimeAssembly", "its target is an assembly known only at run time"),
        ("OpenTarget", "its target is an open generic type"),
        ("NotAnAttributeFilter", "its filter is not an attribute type, an interface or object"),
        ("Indirect", "a delegate or function pointer hides its target and filter from the compile"),
        ("MethodHandle", "a method handle, as an expression tree or reflection uses it, hides its target and filter"),
        ("UnsupportedOverload", "this overload takes another target"),
        ("Provider", "its target is an ICustomAttributeProvider the compile cannot prove to be a Type"),
    ];

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O3)]
    public void EachUnansweredQueryWarnsWithItsReasonAndThrowsWhenReached(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var logs = new CapturingLoggerProvider();
        var application = assets.CompileSource("QueryShapes", Source);

        // Expression.Compile reaches the platform's WIT imports.
        var result = Compile(new(application, [assets.CoreLib], "QueryShapes.EntryPoint", "Run",
            [new("run", "QueryShapes.EntryPoint", "Run")], Optimization: optimization,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"), logs);

        var warnings = logs.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        Assert.All(warnings, warning => Assert.Equal("NW1019", warning.Id.Name));
        // The program creates its own ICustomAttributeProvider, which answers the query through that interface itself, so
        // that warning says when the query throws; every other query throws whatever its target is.
        Assert.All(warnings.Where(warning => !warning.Message.StartsWith("QueryShapes.EntryPoint::Provider ",
            StringComparison.Ordinal)), warning => Assert.Contains(
                "the query throws PlatformNotSupportedException when reached", warning.Message, StringComparison.Ordinal));
        var provider = Assert.Single(warnings, warning => warning.Message.StartsWith("QueryShapes.EntryPoint::Provider ",
            StringComparison.Ordinal)).Message;
        Assert.True(provider.Contains("the query throws PlatformNotSupportedException when its target is one of the " +
            "runtime's own reflection objects, not an object that answers it itself, such as the program's " +
            "QueryShapes.CustomProvider", StringComparison.Ordinal), provider);
        foreach (var (method, reason) in Expected)
        {
            var warning = Assert.Single(warnings, warning => warning.Message.StartsWith(
                $"QueryShapes.EntryPoint::{method} ", StringComparison.Ordinal));
            Assert.Contains(reason, warning.Message, StringComparison.Ordinal);
        }
        Assert.Equal(Expected.Length, warnings.Length);
        // Nine queries throw PlatformNotSupportedException, the reducer answers one, three throw what desktop throws, and
        // the program's own provider answers its query.
        Assert.Equal(1000 + 9 * 100 + 10 + 3, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3,
            WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // The unwritten-static-field fold runs at every level, so the branch is gone and no level warns: what a build
    // reports does not depend on its optimization level.
    [Theory]
    [InlineData(FinalWasmOptimization.None, 0)]
    [InlineData(FinalWasmOptimization.O0, 0)]
    [InlineData(FinalWasmOptimization.O1, 0)]
    [InlineData(FinalWasmOptimization.O3, 0)]
    [InlineData(FinalWasmOptimization.Oz, 0)]
    public void AQueryInABranchTheUnwrittenFieldFoldRemovesWarnsAtNoLevel(FinalWasmOptimization optimization,
        int expected)
    {
        using var assets = TestAssets.Create();
        var logs = new CapturingLoggerProvider();
        var application = assets.CompileSource("FoldedQuery", """
            using System;
            using System.Reflection;
            using System.Runtime.CompilerServices;
            namespace FoldedQuery;
            public sealed class MarkerAttribute : Attribute;
            public static class Switch
            {
                // Nothing writes it, so the whole-program optimizations fold its reads to false.
                public static bool On;
            }
            public static class EntryPoint
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Query(MemberInfo member) =>
                    Switch.On && member.GetCustomAttribute<MarkerAttribute>() is not null ? 1 : 0;

                public static int Run(int input) => Query(typeof(EntryPoint)) + input;
            }
            """);

        var result = Compile(new(application, [assets.CoreLib], "FoldedQuery.EntryPoint", "Run", [],
            Optimization: optimization), logs);

        var warnings = logs.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        Assert.Equal(expected, warnings.Length);
        Assert.All(warnings, warning => Assert.StartsWith("FoldedQuery.EntryPoint::Query calls", warning.Message,
            StringComparison.Ordinal));
        Assert.Equal(3, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3, WasmTarget.Wasm32,
            result.StaticDataEnd));
    }

    // A helper taking a Type answers at every level: called with a Type known only at run time where it is its own
    // method, and with the Type its caller names where inlining moves it into the caller.
    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void AQueryInAnInlinedHelperIsAnsweredAtEveryLevel(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var logs = new CapturingLoggerProvider();
        var application = assets.CompileSource("InlinedQuery", """
            using System;
            namespace InlinedQuery;
            public sealed class MarkerAttribute : Attribute;
            [Marker] public sealed class Target;
            public sealed class Plain;
            public static class EntryPoint
            {
                private static bool Has(Type type) => type.IsDefined(typeof(MarkerAttribute), false);

                public static int Run(int input) => (Has(typeof(Target)) ? 10 : 0) + (Has(typeof(Plain)) ? 100 : 0) + input;
            }
            """);

        var result = Compile(new(application, [assets.CoreLib], "InlinedQuery.EntryPoint", "Run", [],
            Optimization: optimization), logs);

        Assert.DoesNotContain(logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(13, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 3, WasmTarget.Wasm32,
            result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.Linq.Expressions;
        using System.Reflection;
        using System.Runtime.CompilerServices;
        namespace QueryShapes;

        [AttributeUsage(AttributeTargets.All)]
        public sealed class MarkerAttribute : Attribute;
        public interface IMarker;
        [Marker] public sealed class Target;
        [Marker] public sealed class Generic<T>;
        public sealed class FakeParameter : ParameterInfo;
        public sealed class CustomProvider : ICustomAttributeProvider
        {
            public object[] GetCustomAttributes(bool inherit) => [];
            public object[] GetCustomAttributes(Type attributeType, bool inherit) => [new MarkerAttribute()];
            public bool IsDefined(Type attributeType, bool inherit) => true;
        }

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Type Pick(int input) => input > 0 ? typeof(Target) : typeof(EntryPoint);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static Type PickFilter(int input) => input > 0 ? typeof(MarkerAttribute) : typeof(ObsoleteAttribute);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? RunTimeFilter(int input) => typeof(Target).GetCustomAttribute(PickFilter(input));

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? MemberTarget(MemberInfo member) => member.GetCustomAttribute<MarkerAttribute>();

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? EveryAttribute(int input) => Pick(input).GetCustomAttributes(typeof(Attribute), false);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? RunTimeAssembly(Assembly assembly) =>
                assembly.GetCustomAttributes(typeof(MarkerAttribute), false);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? OpenTarget() => typeof(Generic<>).GetCustomAttributes(typeof(MarkerAttribute), false);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? NotAnAttributeFilter() => typeof(Target).GetCustomAttributes(typeof(string), false);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? Indirect()
            {
                Func<MemberInfo, MarkerAttribute?> query = CustomAttributeExtensions.GetCustomAttribute<MarkerAttribute>;
                return query(typeof(Target));
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? MethodHandle()
            {
                Expression<Func<MemberInfo, MarkerAttribute?>> query = member => member.GetCustomAttribute<MarkerAttribute>();
                return query.Compile()(typeof(Target));
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? UnsupportedOverload() =>
                Attribute.GetCustomAttribute(new FakeParameter(), typeof(MarkerAttribute));

            // A target's own query through the interface: the program's provider answers it itself.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? Provider(ICustomAttributeProvider provider) =>
                provider.GetCustomAttributes(typeof(MarkerAttribute), false);

            // The run-time Type reducer answers this one.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? Answered(int input) => Pick(input).GetCustomAttribute<MarkerAttribute>();

            // These throw what desktop throws first.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? NullTarget() => ((Type)null!).GetCustomAttribute<MarkerAttribute>();

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? NullFilter() => typeof(Target).GetCustomAttribute((Type)null!);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static object? InterfaceFilter() => Attribute.IsDefined(typeof(Target), typeof(IMarker));

            private static int Throws<TException>(Func<object?> query) where TException : Exception
            {
                try { query(); return 0; }
                catch (TException) { return 1; }
            }

            public static int Run(int input)
            {
                var unanswered =
                    Throws<PlatformNotSupportedException>(() => RunTimeFilter(input)) +
                    Throws<PlatformNotSupportedException>(() => MemberTarget(typeof(Target))) +
                    Throws<PlatformNotSupportedException>(() => EveryAttribute(input)) +
                    Throws<PlatformNotSupportedException>(() => RunTimeAssembly(typeof(Target).Assembly)) +
                    Throws<PlatformNotSupportedException>(OpenTarget) +
                    Throws<PlatformNotSupportedException>(NotAnAttributeFilter) +
                    Throws<PlatformNotSupportedException>(Indirect) +
                    Throws<PlatformNotSupportedException>(MethodHandle) +
                    Throws<PlatformNotSupportedException>(UnsupportedOverload);
                var answered = Answered(input) is MarkerAttribute ? 1 : 0;
                var provided = Provider(new CustomProvider()) is object[] { Length: 1 } ? 1 : 0;
                var desktop =
                    Throws<ArgumentNullException>(NullTarget) +
                    Throws<ArgumentNullException>(NullFilter) +
                    Throws<ArgumentException>(InterfaceFilter);
                return provided * 1000 + unanswered * 100 + answered * 10 + desktop;
            }
        }
        """;

    private static CompilationResult Compile(CompilerOptions options, CapturingLoggerProvider logs)
    {
        options = options with
        {
            ReferencePaths = [.. TestAssets.WithFrameworkReferences(options.EntryAssemblyPath, options.ReferencePaths)],
        };
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .AddSingleton<ILoggerProvider>(logs)
            .BuildServiceProvider();
        return services.GetRequiredService<INetWasmCompiler>().Compile(options);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<(LogLevel Level, EventId Id, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Entries.Enqueue((logLevel, eventId, formatter(state, exception)));
        }
        public void Dispose() { }
    }
}
