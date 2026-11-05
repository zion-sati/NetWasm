using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.ComponentModel;

/// <summary>
/// Reuses the results of wasm-tools commands that are pure functions of their files:
/// reading a WIT package, a world's dummy core module, and parsing text. Each build
/// asked them of the same WIT packages again, a Node process apiece. A result is kept
/// under the command, its arguments and its input files' contents; only successes
/// are kept, and a command that names a directory always runs.
/// </summary>
public sealed class CachingWasmTools(
    IWasmTools tools,
    ExternalToolCommand command,
    string cacheDirectory) : IWasmTools
{
    private const uint EntryMagic = 0x4357544E;

    private static readonly ImmutableArray<ImmutableArray<string>> PureCommands =
    [
        ["component", "wit"],
        ["component", "embed"],
        ["parse"],
    ];

    private readonly IWasmTools _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    private readonly ExternalToolCommand _command = command ?? throw new ArgumentNullException(nameof(command));
    private readonly string _cacheDirectory = !string.IsNullOrWhiteSpace(cacheDirectory)
        ? cacheDirectory
        : throw new ArgumentException("The wasm-tools cache needs a directory.", nameof(cacheDirectory));

    public ToolResult Run(params IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = arguments.ToImmutableArray();
        var key = Key(values, out var outputPath);
        if (key is null)
        {
            return _tools.Run(values);
        }
        var entryPath = Path.Combine(_cacheDirectory, key[..2], key);
        if (TryRead(entryPath, out var cached, out var output) &&
            (outputPath is null) == (output is null) &&
            TryRestore(outputPath, output))
        {
            return cached;
        }
        var result = _tools.Run(values);
        if (result.ExitCode == 0)
        {
            Write(entryPath, result, outputPath);
        }
        return result;
    }

    // Null when the command is not cached: not a pure command, or it names a directory.
    private string? Key(ImmutableArray<string> arguments, out string? outputPath)
    {
        outputPath = null;
        if (!PureCommands.Any(pure => arguments.Length > pure.Length &&
                arguments.Take(pure.Length).SequenceEqual(pure, StringComparer.Ordinal)))
        {
            return null;
        }
        var text = new StringBuilder("netwasm-wasm-tools-cache-1\n");
        text.Append(_command.Executable).Append('\n');
        foreach (var prefix in _command.ArgumentPrefix)
        {
            text.Append(prefix).Append('\0').Append(Stamp(prefix)).Append('\n');
        }
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if ((argument is "-o" or "--output") && index + 1 < arguments.Length)
            {
                outputPath = arguments[++index];
                text.Append("<output>\n");
            }
            else if (Directory.Exists(argument))
            {
                return null;
            }
            else if (File.Exists(argument))
            {
                text.Append("<file>\0").Append(Path.GetFileName(argument)).Append('\0')
                    .Append(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(argument)))).Append('\n');
            }
            else
            {
                text.Append(argument).Append('\n');
            }
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    // The tool's own files are identified by size and write time, as hashing the tool on
    // every call would cost what the cache saves.
    private static string Stamp(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }
        var file = new FileInfo(path);
        return $"{file.Length}:{file.LastWriteTimeUtc.Ticks}";
    }

    private static bool TryRead(string path, out ToolResult result, out byte[]? output)
    {
        result = null!;
        output = null;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }
            using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
            if (reader.ReadUInt32() != EntryMagic)
            {
                return false;
            }
            result = new ToolResult(reader.ReadInt32(), reader.ReadString(), reader.ReadString());
            if (reader.ReadBoolean())
            {
                var length = reader.ReadInt32();
                output = reader.ReadBytes(length);
                if (output.Length != length)
                {
                    return false;
                }
            }
            return reader.BaseStream.Position == reader.BaseStream.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return false;
        }
    }

    private static bool TryRestore(string? outputPath, byte[]? output)
    {
        if (outputPath is null)
        {
            return true;
        }
        try
        {
            File.WriteAllBytes(outputPath, output!);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Write(string path, ToolResult result, string? outputPath)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var output = outputPath is null ? null : File.ReadAllBytes(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var writer = new BinaryWriter(File.Create(temporary), Encoding.UTF8))
            {
                writer.Write(EntryMagic);
                writer.Write(result.ExitCode);
                writer.Write(result.StandardOutput);
                writer.Write(result.StandardError);
                writer.Write(output is not null);
                if (output is not null)
                {
                    writer.Write(output.Length);
                    writer.Write(output);
                }
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The cache only saves time.
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { }
        }
    }

    /// <summary>The cache directory under the user's local application data, or null without one.</summary>
    public static string? DefaultDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, "NetWasm", "wasm-tools-cache");
    }
}
