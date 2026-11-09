using System.Text;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class PlainCoreModuleImportValidatorTests
{
    [Theory]
    [InlineData("cm32p2|example:host/log")]
    [InlineData("cm64p2|example:host/log")]
    [InlineData("cm32p2|")]
    public void AcceptsTheWorldsOwnFunctions(string module)
    {
        using var files = new ComponentModelTestFiles();
        var path = files.PathFor("plugin.wasm");
        File.WriteAllBytes(path, Module((module, "write")));

        new PlainCoreModuleImportValidator().Validate(path);
        new PlainCoreModuleImportValidator().Validate(Write(files, "empty.wasm", Module()));
    }

    [Theory]
    [InlineData("cm32p2|wasi:cli/stdout@0.3")]
    [InlineData("$root")]
    [InlineData("netwasm.application.v1")]
    [InlineData("cm33p2|example:host/log")]
    [InlineData("|example:host/log")]
    public void RejectsWasiBuiltInsAndAnythingElse(string module)
    {
        using var files = new ComponentModelTestFiles();
        var path = Write(files, "plugin.wasm", Module(("cm32p2|example:host/log", "write"), (module, "f")));

        var exception = Assert.Throws<Core.CompilerException>(() => new PlainCoreModuleImportValidator().Validate(path));

        Assert.Contains($"it imports '{module}#f'", exception.Message);
    }

    [Fact]
    public void RejectsImportsItCannotRead()
    {
        using var files = new ComponentModelTestFiles();
        // A memory import is not a function.
        var path = Write(files, "memory.wasm",
        [
            0, (byte)'a', (byte)'s', (byte)'m', 1, 0, 0, 0,
            2, 11, 1, 3, (byte)'e', (byte)'n', (byte)'v', 3, (byte)'m', (byte)'e', (byte)'m', 2, 0,
        ]);

        Assert.Throws<Core.CompilerException>(() => new PlainCoreModuleImportValidator().Validate(path));
        Assert.Throws<ArgumentException>(() => new PlainCoreModuleImportValidator().Validate(" "));

        // A type section with a GC struct type: the reader does not model it, so the module's
        // imports are not known to be scalar functions.
        var structType = Write(files, "struct.wasm",
        [
            0, (byte)'a', (byte)'s', (byte)'m', 1, 0, 0, 0,
            1, 3, 1, 0x5f, 0,
        ]);
        var exception = Assert.Throws<Core.CompilerException>(
            () => new PlainCoreModuleImportValidator().Validate(structType));
        Assert.Contains("imports must be functions of scalar core types", exception.Message);
    }

    private static string Write(ComponentModelTestFiles files, string name, byte[] bytes)
    {
        var path = files.PathFor(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // A module of one () -> () type and a function import of it per (module, name).
    private static byte[] Module(params (string Module, string Name)[] imports)
    {
        var bytes = new List<byte> { 0, (byte)'a', (byte)'s', (byte)'m', 1, 0, 0, 0, 1, 4, 1, 0x60, 0, 0 };
        if (imports.Length == 0)
        {
            return [.. bytes];
        }
        var section = new List<byte> { (byte)imports.Length };
        foreach (var (module, name) in imports)
        {
            foreach (var text in new[] { module, name })
            {
                var encoded = Encoding.UTF8.GetBytes(text);
                section.Add((byte)encoded.Length);
                section.AddRange(encoded);
            }
            section.Add(0);
            section.Add(0);
        }
        bytes.Add(2);
        bytes.Add((byte)section.Count);
        bytes.AddRange(section);
        return [.. bytes];
    }
}
