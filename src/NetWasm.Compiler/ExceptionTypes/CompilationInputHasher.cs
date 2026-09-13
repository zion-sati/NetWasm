using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NetWasm.Compiler.ExceptionTypes;

public interface ICompilationInputHasher
{
    string Hash(string path);
}

public sealed class CompilationInputHasher : ICompilationInputHasher
{
    public string Hash(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (File.Exists(path))
            return HashFile(path);
        if (!Directory.Exists(path))
            throw new FileNotFoundException(
                "A semantic compiler input does not exist.",
                path);

        var inputs = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(path, file)
                .Replace(Path.DirectorySeparatorChar, '/') + "=" + HashFile(file))
            .Order(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join("\n", inputs))));
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
