using System.Text.RegularExpressions;

namespace NetWasm.Compiler.Tests;

/// <summary>
/// Test-only: appends an asynchronous Main to a fixture whose entry starts asynchronous work and
/// whose observer reads its outcome. Main passes its first argument to the entry, waits on a
/// sentinel delay that is due after any delay the fixture schedules, so the fixture's work runs
/// first, and returns what the observer reads as its exit code. Run as a callback-lifted
/// wasi:cli/run, its work resumes in the host's callbacks as an application's would.
/// </summary>
internal static partial class AsyncObservationEntry
{
    private const string TypeName = "NetWasmAsyncObservationEntry";

    internal const string MethodName = "Main";

    // The appended class joins a file-scoped namespace, so its name depends on the fixture's.
    internal static (string Source, string EntryTypeName) Append(
        string source,
        string entryTypeName,
        string entryMethodName,
        string observeMethodName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryMethodName);
        ArgumentException.ThrowIfNullOrWhiteSpace(observeMethodName);
        var fileScopedNamespace = FileScopedNamespace().Match(source);
        // An observer may read the input again, as the observe export once received it.
        var observeArgument = Regex.IsMatch(
            source, $@"\b{Regex.Escape(observeMethodName)}\s*\(\s*int\s+\w+\s*\)")
            ? "input"
            : string.Empty;
        var program = $$"""


            internal static class {{TypeName}}
            {
                private static async global::System.Threading.Tasks.Task<int> Main(string[] arguments)
                {
                    var text = arguments[0];
                    var magnitude = 0;
                    for (var index = text[0] == '-' ? 1 : 0; index < text.Length; index++)
                    {
                        magnitude = magnitude * 10 + (text[index] - '0');
                    }
                    var input = text[0] == '-' ? -magnitude : magnitude;
                    global::{{entryTypeName}}.{{entryMethodName}}(input);
                    await global::System.Threading.Tasks.Task.Delay(int.MaxValue);
                    return global::{{entryTypeName}}.{{observeMethodName}}({{observeArgument}});
                }
            }
            """;
        return (source + program, fileScopedNamespace.Success
            ? fileScopedNamespace.Groups[1].Value + "." + TypeName
            : TypeName);
    }

    [GeneratedRegex(@"^\s*namespace\s+([\w.]+)\s*;", RegexOptions.Multiline)]
    private static partial Regex FileScopedNamespace();
}
