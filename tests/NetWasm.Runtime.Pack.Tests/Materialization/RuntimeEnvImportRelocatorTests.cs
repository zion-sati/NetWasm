// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Text;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeEnvImportRelocatorTests
{
    private readonly IRuntimeEnvImportRelocator _relocator = Assert.IsAssignableFrom<IRuntimeEnvImportRelocator>(new RuntimeEnvImportRelocator());

    // Only the declared env functions move; the runtime's env function, other modules' imports, non-function imports
    // and every other section stay as they were.
    [Fact]
    public void MovesDeclaredEnvFunctionsToTheirModules()
    {
        var source = Module(
            Function("env", "glClear", 1),
            Function("env", "emscripten_notify_memory_growth", 0),
            Function("webgl", "glClear", 1),
            Table("env", "glViewport"),
            Memory64("env", "memory"),
            Global("env", "glDrawArrays"),
            Tag("env", "glFlush"),
            Function("env", "glViewport", 2));
        var expected = Module(
            Function("webgl", "glClear", 1),
            Function("env", "emscripten_notify_memory_growth", 0),
            Function("webgl", "glClear", 1),
            Table("env", "glViewport"),
            Memory64("env", "memory"),
            Global("env", "glDrawArrays"),
            Tag("env", "glFlush"),
            Function("gl2", "glViewport", 2));

        var relocated = _relocator.Relocate(source, [new("glClear", "webgl"), new("glViewport", "gl2"), new("glFlush", "x")]);

        Assert.Equal(expected, relocated);
    }

    [Fact]
    public void NoImportsLeavesTheModuleUnchanged()
    {
        var source = Module(Function("env", "glClear", 1));

        Assert.Equal(source, _relocator.Relocate(source, []));
        Assert.Throws<ArgumentException>(() => _relocator.Relocate(source, default));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0x61, 0x73, 0x6d, 2, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 0x61, 0x73, 0x6d, 1, 0, 0, 0, 2, 9 })]
    [InlineData(new byte[] { 0, 0x61, 0x73, 0x6d, 1, 0, 0, 0, 2, 3, 1, 1, 0x65 })]
    [InlineData(new byte[] { 0, 0x61, 0x73, 0x6d, 1, 0, 0, 0, 2, 8, 1, 3, 0x65, 0x6e, 0x76, 1, 0x61, 9 })]
    public void RejectsAMalformedModule(byte[] module)
    {
        var error = Assert.Throws<InvalidOperationException>(() => _relocator.Relocate(module, [new("a", "m")]));

        Assert.Contains("import section is invalid", error.Message);
    }

    private static byte[] Module(params byte[][] imports)
    {
        var type = new byte[] { 1, 4, 1, 0x60, 0, 0 }; // a type section ahead of the imports stays as it is
        var payload = new List<byte>();
        payload.AddRange(Unsigned((uint)imports.Length));
        foreach (var import in imports) payload.AddRange(import);
        return
        [
            0, 0x61, 0x73, 0x6d, 1, 0, 0, 0,
            .. type,
            2, .. Unsigned((uint)payload.Count), .. payload,
            0, 4, 3, (byte)'a', (byte)'b', (byte)'c',
        ];
    }

    private static byte[] Function(string module, string name, uint type) => [.. Names(module, name), 0, .. Unsigned(type)];

    private static byte[] Table(string module, string name) => [.. Names(module, name), 1, 0x63, 0x70, 1, 1, 2];

    private static byte[] Memory64(string module, string name) => [.. Names(module, name), 2, 0x0d, 1, 0x80, 0x01, 16];

    private static byte[] Global(string module, string name) => [.. Names(module, name), 3, 0x7f, 1];

    private static byte[] Tag(string module, string name) => [.. Names(module, name), 4, 0, 0];

    private static byte[] Names(string module, string name) =>
        [.. Unsigned((uint)module.Length), .. Encoding.UTF8.GetBytes(module), .. Unsigned((uint)name.Length), .. Encoding.UTF8.GetBytes(name)];

    private static byte[] Unsigned(uint value)
    {
        var bytes = new List<byte>();
        do
        {
            var item = (byte)(value & 0x7f);
            value >>= 7;
            bytes.Add((byte)(item | (value == 0 ? 0 : 0x80)));
        }
        while (value != 0);
        return [.. bytes];
    }
}
