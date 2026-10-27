using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NetWasm.Compiler.Cli;

// One option as csc spells it: -name, -name:value, -name+ or -name-. Name is
// lower case; Value is the text after the colon as written, quotes included,
// "+" or "-", or null. NwcOptionValues removes the quotes as it reads a value,
// so a quoted ',' or ';' stays inside one entry of a list.
internal sealed record NwcOption(string Name, string? Value, string Spelling);

internal sealed record NwcArguments(ImmutableArray<NwcOption> Options, ImmutableArray<string> Inputs)
{
    // What could not be read, in command-line order: unknown options and
    // response-file faults. csc reports them all, after its banner.
    public ImmutableArray<string> Errors { get; init; } = [];
}

// Reads a command line as csc does. @file reads more arguments from a response
// file, one or more to a line, with double quotes grouping text that contains
// spaces and # starting a comment line. Arguments keep their quotes until an
// option's value or an input is read. An argument that starts with - is an
// option; one that starts with / is an option only when it names one, so an
// absolute path stays an input. Option names are case-insensitive.
internal static class NwcArgumentReader
{
    // The options every command takes, besides its own.
    public static readonly FrozenSet<string> CommonOptions =
        new[] { "nologo", "help", "?" }.ToFrozenSet(StringComparer.Ordinal);

    public static NwcArguments Read(
        IReadOnlyList<string> arguments,
        IReadOnlySet<string> knownOptions,
        ITextFileReader files)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(knownOptions);
        ArgumentNullException.ThrowIfNull(files);
        var options = ImmutableArray.CreateBuilder<NwcOption>();
        var inputs = ImmutableArray.CreateBuilder<string>();
        var errors = ImmutableArray.CreateBuilder<string>();
        foreach (var argument in arguments)
        {
            if (!argument.StartsWith('@'))
            {
                Add(argument, knownOptions, options, inputs, errors);
                continue;
            }
            foreach (var expanded in ReadResponseFile(argument[1..], files, errors))
            {
                Add(expanded, knownOptions, options, inputs, errors);
            }
        }
        return new(options.ToImmutable(), inputs.ToImmutable()) { Errors = errors.ToImmutable() };
    }

    private static void Add(
        string argument,
        IReadOnlySet<string> knownOptions,
        ImmutableArray<NwcOption>.Builder options,
        ImmutableArray<string>.Builder inputs,
        ImmutableArray<string>.Builder errors)
    {
        if (argument.Length > 1 && argument[0] is '-' or '/')
        {
            var option = Parse(argument);
            if (knownOptions.Contains(option.Name) || CommonOptions.Contains(option.Name))
            {
                options.Add(option);
                return;
            }
            if (argument[0] == '-')
            {
                errors.Add($"unrecognized option '{argument}'");
                return;
            }
        }
        inputs.Add(Unquote(argument));
    }

    private static NwcOption Parse(string argument)
    {
        var body = argument[1..];
        var colon = body.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            return new(Name(body[..colon]), body[(colon + 1)..], argument);
        }
        return body.Length > 1 && body[^1] is '+' or '-'
            ? new(Name(body[..^1]), body[^1..], argument)
            : new(Name(body), null, argument);
    }

    private static string Name(string name) => name.ToLower(CultureInfo.InvariantCulture);

    // The arguments a response file holds. Its faults are errors in line order:
    // a line that names another response file, or that leaves a quote open.
    private static List<string> ReadResponseFile(
        string path,
        ITextFileReader files,
        ImmutableArray<string>.Builder errors)
    {
        var arguments = new List<string>();
        if (path.Length == 0)
        {
            errors.Add("'@' must name a response file");
            return arguments;
        }
        string text;
        try
        {
            text = files.Read(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"cannot read response file '{path}': {exception.Message}");
            return arguments;
        }
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }
            if (Split(trimmed) is not { } split)
            {
                errors.Add($"a response file line has an unterminated quote: {trimmed}");
                continue;
            }
            foreach (var argument in split)
            {
                if (argument.StartsWith('@'))
                {
                    errors.Add($"a response file cannot name another response file: '{argument}'");
                }
                else
                {
                    arguments.Add(argument);
                }
            }
        }
        return arguments;
    }

    // Splits one response-file line as csc does, at whitespace outside double
    // quotes, keeping the quotes. A quote after an odd number of backslashes is
    // part of the text. A line with an unterminated quote has no arguments. The
    // line is trimmed, so its last character always ends an argument.
    private static List<string>? Split(string line)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var backslashes = 0;
        foreach (var character in line)
        {
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length != 0)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                }
                backslashes = 0;
                continue;
            }
            if (character == '"' && backslashes % 2 == 0)
            {
                quoted = !quoted;
            }
            backslashes = character == '\\' ? backslashes + 1 : 0;
            current.Append(character);
        }
        if (quoted)
        {
            return null;
        }
        arguments.Add(current.ToString());
        return arguments;
    }

    // Removes the quotes from text as csc does. Backslashes are literal unless
    // they precede a quote: 2n of them then are n and the quote groups, and 2n+1
    // are n and a literal quote, which is how MSBuild writes a value that ends in
    // a backslash or holds a quote.
    public static string Unquote(string text)
    {
        var result = new StringBuilder(text.Length);
        var backslashes = 0;
        foreach (var character in text)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                result.Append('\\', backslashes / 2);
                if (backslashes % 2 == 1)
                {
                    result.Append('"');
                }
                backslashes = 0;
                continue;
            }
            result.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        return result.Append('\\', backslashes).ToString();
    }

    // Splits a list value at each ',' and ';' outside quotes, as csc reads
    // -reference, and unquotes the entries; empty entries are dropped.
    public static IEnumerable<string> SplitList(string text)
    {
        var entries = new List<string>();
        var start = 0;
        var quoted = false;
        var backslashes = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"' && backslashes % 2 == 0)
            {
                quoted = !quoted;
            }
            else if (character is ',' or ';' && !quoted)
            {
                entries.Add(text[start..index]);
                start = index + 1;
            }
            backslashes = character == '\\' ? backslashes + 1 : 0;
        }
        entries.Add(text[start..]);
        return entries.Select(Unquote).Where(entry => entry.Length != 0);
    }
}
