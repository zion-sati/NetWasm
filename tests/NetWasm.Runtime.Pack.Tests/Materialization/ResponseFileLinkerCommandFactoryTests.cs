using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

// wasm-ld takes its arguments from a response file, so the command line stays short on Windows (zion-sati/NetWasm#236).
public sealed class ResponseFileLinkerCommandFactoryTests
{
    [Fact]
    public void ArgumentsGoInAPosixQuotedResponseFileNextToTheLog()
    {
        using var directory = new TemporaryDirectory();
        var log = directory.PathTo("logs/link.log");
        var factory = Assert.IsAssignableFrom<ILinkerCommandFactory>(new ResponseFileLinkerCommandFactory());

        var command = factory.Create("wasm-ld", ["--undefined=run", @"C:\with space\it's.o", ""], log);

        var responseFile = Path.ChangeExtension(Path.GetFullPath(log), ".rsp");
        Assert.Equal("wasm-ld", command.ExecutablePath);
        Assert.Equal(log, command.LogPath);
        Assert.Equal(["--rsp-quoting=posix", "@" + responseFile], command.Arguments);
        Assert.Equal(["'--undefined=run'", @"'C:\with space\it'\''s.o'", "''"], File.ReadAllLines(responseFile));
    }

    [Fact]
    public void RejectsAMissingLinkerOrLog()
    {
        var factory = new ResponseFileLinkerCommandFactory();
        Assert.Throws<ArgumentNullException>(() => factory.Create(null!, [], "link.log"));
        Assert.Throws<ArgumentException>(() => factory.Create("wasm-ld", [], ""));
    }

    // wasm-ld reads a path with a space, a single quote and many arguments back exactly: it names the missing input as
    // given.
    [Fact]
    public void WasmLdReadsTheResponseFileBackExactly()
    {
        using var directory = new TemporaryDirectory();
        var emsdk = Environment.GetEnvironmentVariable("NETWASM_EMSDK_ROOT") ??
            Environment.GetEnvironmentVariable("EMSDK") ?? Environment.GetEnvironmentVariable("EMSDK_ROOT") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "emsdk");
        var linker = Path.Combine(emsdk, "upstream", "bin", "wasm-ld" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        Assert.True(File.Exists(linker), linker);
        var input = directory.PathTo("with space/it's missing.o");
        ImmutableArray<string> arguments =
        [
            .. Enumerable.Range(0, 2_000).Select(index => $"--undefined=native_library_entry_point_number_{index:D4}"),
            input,
            "-o", directory.PathTo("out.wasm"),
        ];
        var command = new ResponseFileLinkerCommandFactory().Create(linker, arguments, directory.PathTo("logs/link.log"));

        Assert.Throws<InvalidOperationException>(() => new CommandInvoker().Invoke(command));

        Assert.Contains($"cannot open {input}", File.ReadAllText(command.LogPath), StringComparison.Ordinal);
    }
}
