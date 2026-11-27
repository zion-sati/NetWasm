// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.ComponentModel.Tests.Debugging;

// Builds core modules for the debug line tests: two imported functions of type (i32, i32) -> () (the marker callee 0
// and a decoy 1), then the bodies, each of type () -> (). A body is its instructions without the final end.
internal static class DebugModuleBuilder
{
    public const byte Nop = 0x01;
    public const byte Unreachable = 0x00;
    public const byte End = 0x0b;

    public static byte[] Module(params byte[][] bodies) => Module(true, bodies);

    public static byte[] Module(bool withCode, params byte[][] bodies)
    {
        var module = new List<byte> { 0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00 };
        Section(module, 1, [0x02, 0x60, 0x02, 0x7f, 0x7f, 0x00, 0x60, 0x00, 0x00]);
        Section(module, 2, [0x02, .. Import("marker"), .. Import("decoy")]);
        if (withCode)
        {
            Section(module, 3, [.. Unsigned((uint)bodies.Length), .. bodies.Select(_ => (byte)1)]);
            var code = new List<byte>(Unsigned((uint)bodies.Length));
            foreach (var body in bodies)
            {
                byte[] full = [0x00, .. body, End];
                code.AddRange(Unsigned((uint)full.Length));
                code.AddRange(full);
            }
            Section(module, 10, [.. code]);
        }
        Section(module, 0, [.. Name("producers"), 1, 2, 3]);
        return [.. module];
    }

    // i32.const method; i32.const symbol; call callee.
    public static byte[] Marker(int method, int symbol, uint callee = 0) =>
        [0x41, .. Signed(method), 0x41, .. Signed(symbol), 0x10, .. Unsigned(callee)];

    public static byte[] Instructions(params byte[][] parts) => [.. parts.SelectMany(part => part)];

    public static byte[] Op(params byte[] bytes) => bytes;

    // Where body `index` starts (its locals), relative to the code section's contents of Module(bodies).
    public static int BodyStart(byte[][] bodies, int index)
    {
        var at = Unsigned((uint)bodies.Length).Length;
        for (var body = 0; ; body++)
        {
            var length = bodies[body].Length + 2;
            at += Unsigned((uint)length).Length;
            if (body == index) return at;
            at += length;
        }
    }

    // The address of the instruction at byte `offset` of body `index`.
    public static int Address(byte[][] bodies, int index, int offset) => BodyStart(bodies, index) + 1 + offset;

    public static void Section(List<byte> module, byte id, byte[] payload)
    {
        module.Add(id);
        module.AddRange(Unsigned((uint)payload.Length));
        module.AddRange(payload);
    }

    public static byte[] Name(string name) =>
        [.. Unsigned((uint)System.Text.Encoding.UTF8.GetByteCount(name)), .. System.Text.Encoding.UTF8.GetBytes(name)];

    public static byte[] Unsigned(uint value)
    {
        var output = new List<byte>();
        do
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            output.Add(value == 0 ? next : (byte)(next | 0x80));
        }
        while (value != 0);
        return [.. output];
    }

    public static byte[] Signed(int value)
    {
        var output = new List<byte>();
        while (true)
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            if ((value == 0 && (next & 0x40) == 0) || (value == -1 && (next & 0x40) != 0))
            {
                output.Add(next);
                return [.. output];
            }
            output.Add((byte)(next | 0x80));
        }
    }

    private static byte[] Import(string name) => [.. Name("env"), .. Name(name), 0x00, 0x00];
}
