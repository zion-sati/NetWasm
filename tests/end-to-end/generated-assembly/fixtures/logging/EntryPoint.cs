using System.Globalization;
using Microsoft.Extensions.Logging;

namespace NetWasm.GeneratorChecks.Logging;

public static partial class EntryPoint
{
    [LoggerMessage(EventId = 42, Level = LogLevel.Information, Message = "Count {Count} / {Label}")]
    private static partial void Record(ILogger logger, int count, string label);

    [LoggerMessage(EventId = 43, Level = LogLevel.Error, Message = "Problem {Count}")]
    private static partial void Problem(ILogger logger, int count, Exception exception);

    public static int Run(int input)
    {
        var logger = new CapturingLogger { Enabled = input != 0 };
        Record(logger, input, "captured");
        if (input == 0)
        {
            Problem(logger, input, new InvalidOperationException("disabled"));
            return logger.Calls == 0 ? 42 : -1;
        }

        if (logger.Calls != 1 || logger.Level != LogLevel.Information || logger.EventId != 42 ||
            logger.Message != "Count " + input.ToString(CultureInfo.InvariantCulture) + " / captured" ||
            logger.Exception is not null || logger.StateCount != 3 ||
            logger.FirstName != "Count" || logger.FirstValue is not int value || value != input ||
            logger.Template != "Count {Count} / {Label}")
            return -2;

        var exception = new InvalidOperationException("forwarded");
        Problem(logger, input, exception);
        return logger.Calls == 2 && logger.Level == LogLevel.Error && logger.EventId == 43 &&
            logger.Message == "Problem " + input.ToString(CultureInfo.InvariantCulture) &&
            ReferenceEquals(logger.Exception, exception) && logger.StateCount == 2 &&
            logger.Template == "Problem {Count}" ? 42 : -3;
    }
}

internal sealed class CapturingLogger : ILogger
{
    public bool Enabled { get; init; }
    public int Calls { get; private set; }
    public LogLevel Level { get; private set; }
    public int EventId { get; private set; }
    public string? Message { get; private set; }
    public Exception? Exception { get; private set; }
    public int StateCount { get; private set; }
    public string? FirstName { get; private set; }
    public object? FirstValue { get; private set; }
    public string? Template { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => Enabled;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Calls++;
        Level = logLevel;
        EventId = eventId.Id;
        Message = formatter(state, exception);
        Exception = exception;
        var values = (IReadOnlyList<KeyValuePair<string, object?>>)state!;
        StateCount = values.Count;
        FirstName = values[0].Key;
        FirstValue = values[0].Value;
        Template = (string?)values[^1].Value;
    }
}
