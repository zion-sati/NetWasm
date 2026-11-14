using System;

namespace NetWasm.Runtime.Pack.Materialization;

// The tool's last lines, since NWPACK001 is often all a build log keeps; nothing for a tool that wrote nothing.
internal sealed class ToolOutputExcerptFormatter : IToolOutputExcerptFormatter
{
    private const int Lines = 20;

    public string Format(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var lines = output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        return lines is [""] ? "" : " Its last output:\n" + string.Join('\n', lines[Math.Max(0, lines.Length - Lines)..]);
    }
}
