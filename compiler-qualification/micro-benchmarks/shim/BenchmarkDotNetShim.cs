// Attribute stand-ins for the BenchmarkDotNet surface the dotnet/performance
// micro-benchmarks use. They carry no behaviour: a generated table calls the
// benchmarks directly, so the same sources run without reflection.
using System;

namespace BenchmarkDotNet.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class BenchmarkAttribute : Attribute
    {
        public string? Description { get; set; }
        public bool Baseline { get; set; }
        public int OperationsPerInvoke { get; set; } = 1;
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public sealed class BenchmarkCategoryAttribute(params string[] categories) : Attribute
    {
        public string[] Categories { get; } = categories;
    }

    public sealed class GlobalSetupAttribute : Attribute { public string? Target { get; set; } public string[]? Targets { get; set; } }
    public sealed class GlobalCleanupAttribute : Attribute { public string? Target { get; set; } public string[]? Targets { get; set; } }
    public sealed class IterationSetupAttribute : Attribute { public string? Target { get; set; } public string[]? Targets { get; set; } }
    public sealed class IterationCleanupAttribute : Attribute { public string? Target { get; set; } public string[]? Targets { get; set; } }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class ArgumentsAttribute(params object?[] values) : Attribute { public object?[] Values { get; } = values; }
    public sealed class ArgumentsSourceAttribute(string name) : Attribute { public string Name { get; } = name; }
    public sealed class ParamsAttribute(params object?[] values) : Attribute { public object?[] Values { get; } = values; }
    public sealed class ParamsSourceAttribute(string name) : Attribute { public string Name { get; } = name; }
    public sealed class ParamsAllValuesAttribute : Attribute;
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class GenericTypeArgumentsAttribute(params Type[] types) : Attribute { public Type[] Types { get; } = types; }
    public sealed class MemoryRandomizationAttribute(bool enable = true, int outlierMode = 0) : Attribute;
    public sealed class MinWarmupCountAttribute(int count, bool forceAutoWarmup = false) : Attribute;
    public sealed class MaxWarmupCountAttribute(int count, bool forceAutoWarmup = false) : Attribute;
    public sealed class WarmupCountAttribute(int count) : Attribute;
    public sealed class MinIterationCountAttribute(int count) : Attribute;
    public sealed class MaxIterationCountAttribute(int count) : Attribute;
    public sealed class InvocationCountAttribute(int count, int unrollFactor = 1) : Attribute;
    public sealed class OperationsPerInvokeAttribute(int count) : Attribute;
    public sealed class MinIterationTimeAttribute(int milliseconds) : Attribute;
    public sealed class MemoryDiagnoserAttribute(bool displayGenColumns = true) : Attribute;
    public sealed class AotFilterAttribute(string? reason = null) : Attribute;
    public sealed class GroupBenchmarksByAttribute(params BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule[] rules) : Attribute;
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public sealed class OperatingSystemsFilterAttribute(bool allowed, params BenchmarkDotNet.Extensions.OS[] platforms) : Attribute;
    public sealed class OperatingSystemsArchitectureFilterAttribute(bool allowed, params System.Runtime.InteropServices.Architecture[] architectures) : Attribute;
}

namespace BenchmarkDotNet.Attributes.Filters
{
    public sealed class AnyCategoriesFilterAttribute(params string[] categories) : Attribute;
    public sealed class AllCategoriesFilterAttribute(params string[] categories) : Attribute;
}

namespace BenchmarkDotNet.Configs
{
    public enum BenchmarkLogicalGroupRule { ByMethod, ByJob, ByParams, ByCategory }
}

namespace BenchmarkDotNet.Engines
{
    // Keeps a value alive so the computation that produced it cannot be dropped.
    public sealed class Consumer
    {
        private object? _sink;
        private long _count;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Consume<T>(T value)
        {
            _count++;
            if (!typeof(T).IsValueType) _sink = value;
        }

        public void Consume(object? value) => Consume<object?>(value);
    }
}

namespace BenchmarkDotNet.Running
{
    internal static class RunningNamespace;
}

namespace BenchmarkDotNet.Extensions
{
    public enum OS { Windows, Linux, OSX, Android, iOS, Browser }
}
