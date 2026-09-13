using System.Reflection;
using System.Runtime.Loader;

namespace NetWasm.Testing.OracleHost;

internal interface IOracleAssemblyObserver
{
    Observation Observe(
        string assemblyPath,
        string typeName,
        string methodName,
        int input,
        bool typedTrace,
        string? referencePath);
}

internal sealed class OracleAssemblyObserver : IOracleAssemblyObserver
{
    public Observation Observe(
        string assemblyPath,
        string typeName,
        string methodName,
        int input,
        bool typedTrace,
        string? referencePath)
    {
        var context = new AssemblyLoadContext(
            $"oracle-{Guid.NewGuid():N}",
            isCollectible: true);
        if (referencePath is not null)
        {
            context.Resolving += (_, name) =>
                StringComparer.Ordinal.Equals(
                    name.Name,
                    Path.GetFileNameWithoutExtension(referencePath))
                    ? context.LoadFromAssemblyPath(referencePath)
                    : null;
        }

        Type? type = null;
        try
        {
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            type = assembly.GetType(typeName, throwOnError: true)!;
            var run = Required(type, methodName);
            var value = Invoke(run, input);
            return CreateObservation("value", value, null, null, type, typedTrace);
        }
        catch (TargetInvocationException exception)
            when (exception.InnerException is not null)
        {
            return CreateObservation(
                "exception",
                null,
                exception.InnerException.GetType().FullName,
                exception.InnerException.Message,
                type!,
                typedTrace);
        }
        finally
        {
            context.Unload();
        }
    }

    private static Observation CreateObservation(
        string kind,
        int? value,
        string? exceptionType,
        string? detail,
        Type type,
        bool typedTrace)
    {
        var traceMethod = type.GetMethod(
            "Trace",
            BindingFlags.Public | BindingFlags.Static);
        var trace = traceMethod is null
            ? 0
            : Invoke(traceMethod);
        var records = typedTrace
            ? ReadTypedTrace(type)
            : [new TraceRecord(7, 0, trace, trace < 0 ? -1 : 0)];
        return new(kind, value, exceptionType, trace, detail, records);
    }

    private static TraceRecord[] ReadTypedTrace(Type type)
    {
        var count = Invoke(Required(type, "TraceCount"));
        var kind = Required(type, "TraceKind");
        var eventId = Required(type, "TraceEventId");
        var low = Required(type, "TracePayloadLow");
        var high = Required(type, "TracePayloadHigh");
        var records = new TraceRecord[count];
        for (var index = 0; index < count; index++)
        {
            records[index] = new(
                Invoke(kind, index),
                Invoke(eventId, index),
                Invoke(low, index),
                Invoke(high, index));
        }
        return records;
    }

    private static MethodInfo Required(Type type, string name) =>
        type.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException($"fixture does not expose {name}");

    private static int Invoke(MethodInfo method, params object[] arguments) =>
        (int)(method.Invoke(null, arguments)
            ?? throw new InvalidOperationException($"{method.Name} returned null"));
}
