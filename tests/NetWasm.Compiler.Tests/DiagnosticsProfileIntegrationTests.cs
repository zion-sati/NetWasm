using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using System.Text;
using System.Text.RegularExpressions;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticsProfileIntegrationTests
{
    [Fact]
    public void ActivityEntropyWithoutCapabilityBoundaryFailsDeterministically()
    {
        using var assets = TestAssets.Create();
        var entryAssembly = assets.CompileSource("ActivityEntropyDenied", """
            using System.Diagnostics;

            namespace NetWasm.Diagnostics.Fixtures;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    ActivityTraceId traceId = ActivityTraceId.CreateRandom();
                    return traceId == default ? input : input + 1;
                }
            }
            """);
        var options = CompilerTestSupport.Options(
            assets,
            [],
            "NetWasm.Diagnostics.Fixtures.EntryPoint",
            "Run") with
        {
            EntryAssemblyPath = entryAssembly,
            ReferencePaths = [assets.CoreLib],
        };

        var exception = Assert.Throws<CompilerException>(() => NetWasmCompiler.Compile(options));
        Assert.Equal(DiagnosticCode.ComponentContract, exception.Diagnostic.Code);
    }

    [Fact]
    public void RootedProfilesRetainOnlyReachableDiagnosticsFamilies()
    {
        using var assets = TestAssets.Create();
        var activity = CompileRoot(assets, "ActivityRoot", """
            using System.Diagnostics;

            namespace NetWasm.Diagnostics.Fixtures;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    ActivityTraceId traceId = ActivityTraceId.CreateFromString(
                        "0123456789abcdef0123456789abcdef");
                    return traceId == default ? input : input + 1;
                }
            }
            """);
        var metrics = CompileRoot(assets, "MetricsRoot", """
            using System.Diagnostics.Metrics;

            namespace NetWasm.Diagnostics.Fixtures;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Meter meter = new("metrics-root");
                    return meter.Name.Length + input;
                }
            }
            """);
        var none = CompileRoot(assets, "NoDiagnosticsRoot", """
            namespace NetWasm.Diagnostics.Fixtures;

            public static class EntryPoint
            {
                public static int Run(int input) => 42 + input;
            }
            """);

        var activityMethods = ReachableMethodNames(activity);
        var metricsMethods = ReachableMethodNames(metrics);
        var noneMethods = ReachableMethodNames(none);
        var noneTypes = ReachableTypeNames(none);
        var activityImports = ReachableImportNames(activity);
        var metricsImports = ReachableImportNames(metrics);
        var noneImports = ReachableImportNames(none);

        Assert.Contains(activityMethods, name => name.Contains("System.Diagnostics.ActivityTraceId", StringComparison.Ordinal));
        Assert.DoesNotContain(activityMethods, name => name.Contains("System.Diagnostics.Metrics", StringComparison.Ordinal));
        Assert.DoesNotContain(activityImports, name => name.Contains("random", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(metricsMethods, name => name.Contains("System.Diagnostics.Activity", StringComparison.Ordinal));
        Assert.Contains(metricsMethods, name => name.Contains("System.Diagnostics.Metrics.Meter", StringComparison.Ordinal));
        Assert.DoesNotContain(metricsImports, name => name.Contains("random", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(metricsImports, name => name.Contains("clock", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(noneMethods, name => name.Contains("System.Diagnostics.Activity", StringComparison.Ordinal));
        Assert.DoesNotContain(noneMethods, name => name.Contains("System.Diagnostics.Metrics", StringComparison.Ordinal));
        Assert.DoesNotContain(noneMethods, name => name.Contains("System.Diagnostics", StringComparison.Ordinal));
        Assert.DoesNotContain(noneTypes, name => name.Contains("System.Diagnostics", StringComparison.Ordinal));
        Assert.Empty(noneImports);
        Assert.Empty(none.Program.WitImportMethods);
        Assert.Empty(none.Program.JSImportMethods);
        Assert.DoesNotContain(none.Program.StringLiterals, value => value.Contains(
            "System.Diagnostics", StringComparison.Ordinal));
        Assert.DoesNotContain(none.Program.MethodInstances.Values, method => method.Definition.DeclaringType.Assembly.Name.Contains(
            "System.Diagnostics", StringComparison.Ordinal));
        Assert.DoesNotContain(SerializedArtifactTexts(none), value => value.Contains(
            "System.Diagnostics", StringComparison.Ordinal));
        Assert.DoesNotContain(Encoding.UTF8.GetString(none.ApplicationModule),
            "System.Diagnostics", StringComparison.Ordinal);

        foreach (var profile in new[] { activity, metrics, none })
        {
            var methods = ReachableMethodNames(profile);
            Assert.DoesNotContain(methods, name => name.Contains("EventSource", StringComparison.Ordinal));
            Assert.DoesNotContain(methods, name => name.Contains("EventPipe", StringComparison.Ordinal));
            Assert.DoesNotContain(methods, name => name.Contains("System.Reflection", StringComparison.Ordinal));
            Assert.DoesNotContain(methods, name => name.Contains("JavaScript", StringComparison.Ordinal));
            Assert.DoesNotContain(profile.Program.WitImportMethods,
                method => method.WitImport?.InterfaceName.Contains("javascript", StringComparison.OrdinalIgnoreCase) == true ||
                    method.WitImport?.FunctionName.Contains("javascript", StringComparison.OrdinalIgnoreCase) == true);
            Assert.DoesNotContain(Encoding.UTF8.GetString(profile.ApplicationModule),
                "EventSource", StringComparison.Ordinal);
            Assert.DoesNotContain(Encoding.UTF8.GetString(profile.ApplicationModule),
                "EventPipe", StringComparison.Ordinal);
            AssertNoGeneratedDiagnosticsMetadata(profile);
        }
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void ControlledHeaderConsumerExecutesLinkedNetWasmTracingClosure(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var result = CompileControlledHeaderRoot(assets, "ControlledHeaderConsumer", """
            using System;
            using System.Collections.Generic;
            using System.Diagnostics;

            namespace NetWasm.Diagnostics.Fixtures;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var firstRoot = new Activity("first-root")
                        .SetIdFormat(ActivityIdFormat.Hierarchical)
                        .Start();
                    string firstRootId = firstRoot.Id!;
                    var child = new Activity("child").Start();
                    string childId = child.Id!;
                    child.Stop();
                    firstRoot.Stop();

                    var secondRoot = new Activity("second-root")
                        .SetIdFormat(ActivityIdFormat.Hierarchical)
                        .Start();
                    string secondRootId = secondRoot.Id!;
                    secondRoot.Stop();

                    int firstSuffix = firstRootId.IndexOf('-');
                    int secondSuffix = secondRootId.IndexOf('-');
                    bool hierarchicalRootsAreValid =
                        firstRootId.StartsWith("|", StringComparison.Ordinal) &&
                        firstRootId.EndsWith(".", StringComparison.Ordinal) &&
                        secondRootId.StartsWith("|", StringComparison.Ordinal) &&
                        secondRootId.EndsWith(".", StringComparison.Ordinal) &&
                        firstSuffix > 1 &&
                        secondSuffix > 1 &&
                        firstRootId.Substring(firstSuffix) == secondRootId.Substring(secondSuffix) &&
                        firstRootId != secondRootId &&
                        childId.StartsWith(firstRootId, StringComparison.Ordinal) &&
                        childId != firstRootId;

                    const string parentSpanId = "00f067aa0ba902b7";
                    var activity = new Activity("controlled-consumer")
                        .SetIdFormat(ActivityIdFormat.W3C)
                        .SetParentId("00-4bf92f3577b34da6a3ce929d0e0e4736-" + parentSpanId + "-01");
                    activity.Start();
                    var propagator = DistributedContextPropagator.CreateW3CPropagator();
                    var carrier = new HeaderCarrier
                    {
                        TraceState = "vendor=value",
                    };
                    propagator.Inject(activity, carrier, SetHeader);
                    propagator.ExtractTraceIdAndState(carrier, GetHeader, out var traceParent, out var traceState);
                    activity.Stop();
                    var propagatedSpanId = traceParent is not null && traceParent.Length >= 52
                        ? traceParent.Substring(36, 16)
                        : null;
                    var negativeCasesReject =
                        !IsDistinctNonZeroLowerHex("0000000000000000", parentSpanId) &&
                        !IsDistinctNonZeroLowerHex(parentSpanId, parentSpanId) &&
                        !IsDistinctNonZeroLowerHex("not-a-span-id", parentSpanId);
                    var valid = hierarchicalRootsAreValid &&
                        traceParent is not null &&
                        traceParent.Length == 55 &&
                        traceParent.StartsWith("00-4bf92f3577b34da6a3ce929d0e0e4736-", StringComparison.Ordinal) &&
                        traceParent.EndsWith("-01", StringComparison.Ordinal) &&
                        traceState == "vendor=value" &&
                        IsDistinctNonZeroLowerHex(propagatedSpanId, parentSpanId) &&
                        negativeCasesReject;
                    return valid ? input + 100 : input - 100;
                }

                private static bool IsDistinctNonZeroLowerHex(string? spanId, string parentSpanId)
                {
                    if (spanId is null || spanId.Length != 16 ||
                        spanId == "0000000000000000" || spanId == parentSpanId)
                    {
                        return false;
                    }

                    for (var index = 0; index < spanId.Length; index++)
                    {
                        var character = spanId[index];
                        if (!((character >= '0' && character <= '9') ||
                            (character >= 'a' && character <= 'f')))
                        {
                            return false;
                        }
                    }

                    return true;
                }

                private static void SetHeader(object? carrier, string name, string value)
                {
                    var headers = (HeaderCarrier)carrier!;
                    if (name == "traceparent")
                    {
                        headers.TraceParent = value;
                    }
                    else if (name == "tracestate")
                    {
                        headers.TraceState = value;
                    }
                }

                private static void GetHeader(
                    object? carrier,
                    string name,
                    out string? value,
                    out IEnumerable<string>? values)
                {
                    var headers = (HeaderCarrier)carrier!;
                    value = name == "traceparent" ? headers.TraceParent :
                        name == "tracestate" ? headers.TraceState : null;
                    values = null;
                }

                private sealed class HeaderCarrier
                {
                    public string? TraceParent;
                    public string? TraceState;
                }
            }
            """, optimize, target);

        var diagnostics = result.Program.Methods.Values
            .Concat(result.Program.ConstructedMethods.Values)
            .Select(method => method.Method)
            .Where(instance => instance.DeclaringType.CanonicalName.Contains(
                "System.Diagnostics", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(diagnostics, instance => instance.DeclaringType.CanonicalName.Contains(
            "System.Diagnostics.Activity", StringComparison.Ordinal));
        Assert.Contains(diagnostics, instance => instance.DeclaringType.CanonicalName.Contains(
            "System.Diagnostics.DistributedContextPropagator", StringComparison.Ordinal));
        Assert.Contains(diagnostics, instance => instance.DeclaringType.CanonicalName.Contains(
            "System.Diagnostics.W3CPropagator", StringComparison.Ordinal));
        var coreLibAssembly = Path.GetFileNameWithoutExtension(assets.CoreLib);
        Assert.All(diagnostics, instance => Assert.Equal(
            coreLibAssembly,
            instance.Definition.DeclaringType.Assembly.Name));
        Assert.DoesNotContain(diagnostics, instance => instance.Definition.DeclaringType.Assembly.Name ==
            "System.Diagnostics.DiagnosticSource");
        Assert.Contains(
            ReachableImportNames(result),
            import => import.Contains("wasi:random", StringComparison.Ordinal));
        Assert.Empty(result.Program.JSImportMethods);
        Assert.DoesNotContain(ReachableImportNames(result), import =>
            import.Contains("http", StringComparison.OrdinalIgnoreCase) ||
            import.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            import.Contains("JSImport", StringComparison.Ordinal) ||
            import.Contains("JSExport", StringComparison.Ordinal));
        var moduleText = Encoding.UTF8.GetString(result.ApplicationModule);
        Assert.DoesNotContain(moduleText, "System.Diagnostics.DiagnosticSource", StringComparison.Ordinal);
        AssertNoGeneratedDiagnosticsMetadata(result);
        Assert.Equal(107, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            7,
            target,
            result.StaticDataEnd));
    }

    private static CompilationResult CompileRoot(TestAssets assets, string assemblyName, string source)
    {
        Assert.True(
            Regex.IsMatch(
                source,
                @"public\s+static\s+int\s+Run\s*\(\s*int(?:\s+\w+)?\s*\)",
                RegexOptions.CultureInvariant),
            $"fixture '{assemblyName}' must expose the established static int Run(int) entry contract");
        var entryAssembly = assets.CompileSource(assemblyName, source);
        var entryType = "NetWasm.Diagnostics.Fixtures.EntryPoint";
        var options = CompilerTestSupport.Options(assets, [], entryType, "Run") with
        {
            EntryAssemblyPath = entryAssembly,
            ReferencePaths = [assets.CoreLib],
        };
        return NetWasmCompiler.Compile(options);
    }

    private static CompilationResult CompileControlledHeaderRoot(
        TestAssets assets,
        string assemblyName,
        string source,
        bool optimize = false,
        WasmTarget target = WasmTarget.Wasm32)
    {
        Assert.True(
            Regex.IsMatch(
                source,
                @"public\s+static\s+int\s+Run\s*\(\s*int(?:\s+\w+)?\s*\)",
                RegexOptions.CultureInvariant),
            $"fixture '{assemblyName}' must expose the established static int Run(int) entry contract");
        var entryAssembly = optimize
            ? assets.CompileOptimizedSource(assemblyName, source)
            : assets.CompileSource(assemblyName, source);
        var entryType = "NetWasm.Diagnostics.Fixtures.EntryPoint";
        var options = CompilerTestSupport.CreateReactorOptions(
            assets,
            entryAssembly,
            entryType,
            "Run") with
        {
            EntryAssemblyPath = entryAssembly,
            ReferencePaths = [assets.CoreLib],
            Target = target,
            WitWorld = "netwasm:platform/platform@1.0.0",
        };
        return NetWasmCompiler.Compile(options);
    }

    private static HashSet<string> ReachableMethodNames(CompilationResult result) =>
        result.Program.MethodInstances.Values.Select(method => method.CanonicalName)
            .Concat(result.Program.ConstructedMethods.Keys)
            .Concat(result.Program.Methods.Values
                .Select(method => method.Method.CanonicalName))
            .Concat(result.Program.ConstructedMethods.Values
                .Select(method => method.Method.CanonicalName))
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> ReachableTypeNames(CompilationResult result) =>
        result.Program.ConstructedTypes.Select(type => type.CanonicalName)
            .Concat(result.Program.MethodInstances.Values.Select(method => method.DeclaringType.CanonicalName))
            .Concat(result.Program.Methods.Values
                .Select(method => method.Method.DeclaringType.CanonicalName))
            .Concat(result.Program.ConstructedMethods.Values
                .Select(method => method.Method.DeclaringType.CanonicalName))
            .ToHashSet(StringComparer.Ordinal);

    private static void AssertNoGeneratedDiagnosticsMetadata(CompilationResult result)
    {
        var forbiddenMetadataTokens = new[]
        {
            "DiagnosticTypeNameTable",
            "GeneratedDiagnosticsMetadata",
            "System.Diagnostics.DiagnosticSource",
            "Type.FullName",
            "Type.Name",
            "Type.Namespace",
        };
        foreach (var token in forbiddenMetadataTokens)
        {
            Assert.DoesNotContain(SerializedArtifactTexts(result),
                value => value.Contains(token, StringComparison.Ordinal));
        }
    }

    private static IEnumerable<string> SerializedArtifactTexts(CompilationResult result)
    {
        yield return Encoding.UTF8.GetString(result.ApplicationModule);
        foreach (var literal in result.Program.StringLiterals)
        {
            yield return literal;
        }

        if (result.DiagnosticArtifacts is { } artifacts)
        {
            yield return Encoding.UTF8.GetString(artifacts.Wasm);
            yield return Encoding.UTF8.GetString(artifacts.ExceptionTypeMap.Bytes);
            yield return Encoding.UTF8.GetString(artifacts.ManifestBytes);
        }
    }

    private static HashSet<string> ReachableImportNames(CompilationResult result) =>
        result.Program.WitImportMethods
            .Select(method => $"{method.WitImport?.InterfaceName}/{method.WitImport?.FunctionName}")
            .Concat(result.Program.JSImportMethods.Select(method => $"{method.JSImport?.ModuleName}/{method.JSImport?.FunctionName}"))
            .ToHashSet(StringComparer.Ordinal);
}
