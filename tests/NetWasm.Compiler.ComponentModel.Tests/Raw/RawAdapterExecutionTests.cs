using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

// Runs the canonical-ABI code a raw adapter emits for its callable imports, in
// Node against real linear memory and the host's resource store.
public sealed class RawAdapterExecutionTests
{
    private const string Interface = "sample:raw@1.0.0/api";
    private const string DeploymentInterface = "sample:raw/api@1.0.0";

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LiftsFlatScalarsAndValidatesAFlatResult(WasmTarget target)
    {
        Run(target, [], [
            Function("scalars", [
                ("a", P("bool")), ("b", P("s8")), ("c", P("u8")), ("d", P("s16")), ("e", P("u16")),
                ("f", P("s32")), ("g", P("u32")), ("h", P("s64")), ("i", P("u64")), ("j", P("f32")),
                ("k", P("f64")), ("l", P("char")),
            ], P("u32")),
        ], """
            let received;
            let result = 4294967295;
            const { imports } = await open({ "sample:raw/api@1.0.0": {
              scalars(...values) { received = values; return result; },
            } });
            assert.equal(imports.scalars(1, -1, 300, 0x18000, -1, -5, -1, -2n, -1n, 1.1, Math.PI, 0x1f600), -1);
            assert.deepEqual(received, [
              true, -1, 44, -32768, 65535, -5, 4294967295, -2n, 18446744073709551615n,
              Math.fround(1.1), Math.PI, "\u{1f600}",
            ]);
            result = -1;
            assert.throws(() => imports.scalars(0, 0, 0, 0, 0, 0, 0, 0n, 0n, 0, 0, 65), /u32 value is invalid/);
            result = 7;
            assert.throws(() => imports.scalars(0, 0, 0, 0, 0, 0, 0, 0n, 0n, 0, 0, 0xd800), /code point is invalid/);
            """);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ReadsTextAndListsAndWritesAnIndirectListOfText(WasmTarget target)
    {
        Run(target, [
            ("bytes", """{"list":"u8"}"""),
            ("words", """{"list":"u16"}"""),
            ("names", """{"list":"string"}"""),
        ], [
            Function("join", [("name", P("string")), ("bytes", D(0)), ("words", D(1))], D(2)),
        ], """
            let received;
            const harness = await open({ "sample:raw/api@1.0.0": {
              join(...values) { received = values; return ["a", "βγ"]; },
            } });
            const { imports, address, view, readPointer, readBytes, reallocations, wasm64 } = harness;
            const name = new TextEncoder().encode("héllo");
            harness.writeBytes(64, name);
            harness.writeBytes(128, [1, 2, 3]);
            view().setUint16(256, 1, true);
            view().setUint16(258, 65535, true);
            const length = value => wasm64 ? BigInt(value) : value;
            assert.equal(imports.join(address(64), length(name.length), address(128), length(3),
              address(256), length(2), address(512)), undefined);
            assert.equal(received[0], "héllo");
            assert.ok(received[1] instanceof Uint8Array);
            assert.deepEqual([...received[1]], [1, 2, 3]);
            assert.deepEqual(received[2], [1, 65535]);
            const pointer = wasm64 ? 8 : 4;
            const list = readPointer(512);
            assert.equal(readPointer(512 + pointer), 2);
            const text = index => new TextDecoder().decode(readBytes(
              readPointer(list + index * pointer * 2), readPointer(list + index * pointer * 2 + pointer)));
            assert.deepEqual([text(0), text(1)], ["a", "βγ"]);
            // The list's elements are allocated before its strings.
            assert.deepEqual(reallocations.map(call => call.slice(2)),
              [[pointer, pointer * 4], [1, 1], [1, 4]]);
            assert.throws(() => imports.join(address(64), length(1), address(128), length(1),
              address(257), length(1), address(512)), /unaligned/);
            harness.writeBytes(64, [0xff]);
            assert.throws(() => imports.join(address(64), length(1), address(128), length(0),
              address(256), length(0), address(512)), /UTF-8 text is invalid/);
            """);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LiftsAndLowersRecordsVariantsEnumsFlagsAndResults(WasmTarget target)
    {
        Run(target, [
            ("point", """{"record":{"fields":[{"name":"x-value","type":"s32"},{"name":"y","type":"f64"}]}}"""),
            ("shape", """{"variant":{"cases":[{"name":"circle","type":"f32"},{"name":"square","type":"u64"},{"name":"empty","type":null}]}}"""),
            ("color", """{"enum":{"cases":[{"name":"red"},{"name":"green"},{"name":"blue"}]}}"""),
            ("perms", """{"flags":{"flags":[{"name":"read"},{"name":"write-all"},{"name":"exec"}]}}"""),
            ("pair", """{"tuple":{"types":["u8","bool"]}}"""),
            ("maybe", """{"option":"u32"}"""),
            ("inner", """{"option":"u8"}"""),
            ("outer", """{"option":6}"""),
            ("outcome", """{"result":{"ok":0,"err":"string"}}"""),
        ], [
            Function("shapes", [
                ("p", D(0)), ("s", D(1)), ("c", D(2)), ("f", D(3)), ("t", D(4)), ("o", D(5)), ("n", D(7)),
            ], D(8)),
        ], """
            let received;
            let behaviour = () => ({ xValue: -3, y: 2.5 });
            const harness = await open({ "sample:raw/api@1.0.0": {
              shapes(...values) { received = values; return behaviour(); },
            } });
            const { imports, address, view, readPointer, readBytes } = harness;
            const scratch = new DataView(new ArrayBuffer(8));
            scratch.setFloat32(0, 1.5, true);
            const circle = BigInt(scratch.getUint32(0, true));
            const call = (shape, payload) => imports.shapes(
              -7, 0.25, shape, payload, 2, 0b101, 300, 1, 1, 9, 1, 1, 4, address(1024));
            assert.equal(call(0, circle), undefined);
            assert.deepEqual(received, [
              { xValue: -7, y: 0.25 },
              { tag: "circle", val: 1.5 },
              "blue",
              { read: true, writeAll: false, exec: true },
              [44, true],
              9,
              { tag: "some", val: 4 },
            ]);
            assert.equal(view().getUint8(1024), 0);
            assert.equal(view().getInt32(1032, true), -3);
            assert.equal(view().getFloat64(1040, true), 2.5);
            call(1, 18446744073709551615n);
            assert.deepEqual(received[1], { tag: "square", val: 18446744073709551615n });
            call(2, 0n);
            assert.deepEqual(received[1], { tag: "empty" });
            assert.throws(() => call(3, 0n), /variant discriminant is invalid/);
            behaviour = () => { throw Object.assign(new Error("no"), { payload: "bad" }); };
            call(2, 0n);
            assert.equal(view().getUint8(1024), 1);
            // The point's f64 aligns the payload at 8.
            const text = readPointer(1032);
            assert.equal(new TextDecoder().decode(readBytes(text, 3)), "bad");
            behaviour = () => { throw new RangeError("host failure"); };
            assert.throws(() => call(2, 0n), /host failure/);
            behaviour = () => ({ xValue: 1 });
            assert.throws(() => call(2, 0n), /record value is invalid/);
            """);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ConstructsBorrowsAndTransfersResources(WasmTarget target)
    {
        Run(target, [
            ("file-handle", "\"resource\""),
            (null, """{"handle":{"own":0}}"""),
            (null, """{"handle":{"borrow":0}}"""),
        ], [
            Function("[constructor]file-handle", [("name", P("string"))], D(1), new("constructor", 0)),
            Function("[method]file-handle.size", [("self", D(2))], P("u32"), new("method", 0)),
            Function("consume", [("first", D(1)), ("second", D(1))], P("u32")),
        ], """
            class FileHandle {
              constructor(name) { this.name = name; }
              size() { return this.name.length; }
            }
            let consumed;
            const harness = await open({ "sample:raw/api@1.0.0": {
              FileHandle,
              consume(first, second) { consumed = [first, second]; return 3; },
            } });
            const { imports, address, wasm64 } = harness;
            harness.writeBytes(64, new TextEncoder().encode("abcd"));
            const length = value => wasm64 ? BigInt(value) : value;
            const first = imports["[constructor]file-handle"](address(64), length(4));
            const second = imports["[constructor]file-handle"](address(64), length(2));
            assert.ok(first > 0 && second > first);
            assert.ok(harness.borrow(0, first) instanceof FileHandle);
            assert.equal(imports["[method]file-handle.size"](first), 4);
            assert.throws(() => imports.consume(first, first), /owned resource is duplicated/);
            assert.ok(harness.borrow(0, first) instanceof FileHandle);
            assert.equal(imports.consume(first, second), 3);
            assert.equal(consumed[0].name, "abcd");
            assert.throws(() => harness.borrow(0, first));
            assert.throws(() => imports["[method]file-handle.size"](second));
            """);
    }

    [Fact]
    public void RollsBackItsAllocationsAndResourcesWhenLoweringFails()
    {
        Run(WasmTarget.Wasm32, [
            ("file-handle", "\"resource\""),
            (null, """{"handle":{"own":0}}"""),
            ("bundle", """{"tuple":{"types":[1,3]}}"""),
            ("names", """{"list":"string"}"""),
        ], [
            Function("make", [], D(2)),
        ], """
            class FileHandle {}
            let names = ["one", "two"];
            const harness = await open({ "sample:raw/api@1.0.0": {
              FileHandle,
              make() { return [new FileHandle(), names]; },
            } });
            const { imports, reallocations, resources } = harness;
            assert.equal(imports.make(512), undefined);
            const handle = harness.view().getUint32(512, true);
            assert.ok(harness.borrow(0, handle) instanceof FileHandle);
            names = ["ok", 5];
            const before = reallocations.length;
            assert.throws(() => imports.make(512), /text value is invalid/);
            assert.equal(reallocations.length, before);
            // A text whose bytes fall outside memory fails after the list allocated.
            names = ["x".repeat(70_000_000)];
            assert.throws(() => imports.make(512), RangeError);
            const frees = reallocations.slice(before).filter(call => call[3] === 0);
            assert.equal(frees.length, 1);
            """, heapStart: 1024);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ReadsParametersBeyondTheFlatLimitFromTheirTuple(WasmTarget target)
    {
        Run(target, [], [
            Function("many", [.. Enumerable.Range(0, 17).Select(index => ($"value-{index}", P("u32")))], null),
        ], """
            let received;
            const harness = await open({ "sample:raw/api@1.0.0": {
              many(...values) { received = values; },
            } });
            for (let index = 0; index < 17; index++) harness.view().setUint32(128 + index * 4, index * 10, true);
            assert.equal(harness.imports.many(harness.address(128)), undefined);
            assert.deepEqual(received, Array.from({ length: 17 }, (_, index) => index * 10));
            assert.throws(() => harness.imports.many(harness.address(130)), /unaligned/);
            """);
    }

    [Fact]
    public void EmitsTheSameCodeForTheSameTypeOnce()
    {
        var source = Write(WasmTarget.Wasm32, [("names", """{"list":"string"}""")], [
            Function("first", [("names", D(0))], null),
            Function("second", [("names", D(0))], null),
        ]);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(source, "^function list\\d+\\(",
            System.Text.RegularExpressions.RegexOptions.Multiline));
        Assert.Contains("function bindCallable0(host)", source, StringComparison.Ordinal);
        Assert.Contains("function bindCallable1(host)", source, StringComparison.Ordinal);
        Assert.Contains("\"requiredCapabilities\":[\"canonical\"]", source, StringComparison.Ordinal);
        Assert.Contains("\"abiVersion\":2", source, StringComparison.Ordinal);
        Assert.DoesNotContain("export function rc", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LowersEveryKindThatFitsOneCoreValueAsAFlatResult(WasmTarget target)
    {
        Run(target, [
            ("file-handle", "\"resource\""),
            (null, """{"handle":{"own":0}}"""),
            (null, """{"handle":{"borrow":0}}"""),
            ("color", """{"enum":{"cases":[{"name":"red"},{"name":"green"},{"name":"blue"}]}}"""),
            ("perms", """{"flags":{"flags":[{"name":"read"},{"name":"write-all"},{"name":"exec"}]}}"""),
            ("small", """{"type":"u16"}"""),
            ("only", """{"record":{"fields":[{"name":"value","type":"s16"}]}}"""),
            ("single", """{"tuple":{"types":["u8"]}}"""),
            ("status", """{"result":{"ok":null,"err":null}}"""),
            ("pick", """{"variant":{"cases":[{"name":"first","type":null},{"name":"second","type":null}]}}"""),
            ("lent", """{"record":{"fields":[{"name":"handle","type":2},{"name":"count","type":"u32"}]}}"""),
        ], [
            Function("rbool", [], P("bool")),
            Function("rsbyte", [], P("s8")),
            Function("rbyte", [], P("u8")),
            Function("rshort", [], P("s16")),
            Function("rushort", [], P("u16")),
            Function("rint", [], P("s32")),
            Function("rlong", [], P("s64")),
            Function("rulong", [], P("u64")),
            Function("rsingle", [], P("f32")),
            Function("rdouble", [], P("f64")),
            Function("rchar", [], P("char")),
            Function("renum", [], D(3)),
            Function("rflags", [], D(4)),
            Function("ralias", [], D(5)),
            Function("rrecord", [], D(6)),
            Function("rtuple", [], D(7)),
            Function("rresult", [], D(8)),
            Function("rvariant", [], D(9)),
            Function("rown", [], D(1)),
            Function("rborrow", [], D(2)),
            Function("rlent", [], D(10)),
        ], """
            class FileHandle {}
            const owned = new FileHandle();
            let failing = false;
            const values = {
              rbool: true, rsbyte: -5, rbyte: 200, rshort: -300, rushort: 60000, rint: -7, rlong: -2n,
              rulong: 18446744073709551615n, rsingle: 1.1, rdouble: Math.PI, rchar: "\u{1f600}", renum: "green",
              rflags: { read: true, writeAll: false, exec: true }, ralias: 1234, rrecord: { value: -9 }, rtuple: [7],
              rvariant: { tag: "second" }, rown: owned, rborrow: owned, rlent: { handle: owned, count: 1 },
            };
            const providers = Object.fromEntries(Object.entries(values).map(([name, value]) => [name, () => value]));
            providers.rresult = () => {
              if (failing) throw Object.assign(new Error("no"), { payload: undefined });
            };
            const harness = await open({ "sample:raw/api@1.0.0": { FileHandle, ...providers } });
            const { imports } = harness;
            assert.deepEqual(
              ["rbool", "rsbyte", "rbyte", "rshort", "rushort", "rint", "rlong", "rulong", "rsingle", "rdouble",
                "rchar", "renum", "rflags", "ralias", "rrecord", "rtuple", "rvariant", "rresult"].map(name => imports[name]()),
              [1, -5, 200, -300, 60000, -7, -2n, -1n, Math.fround(1.1), Math.PI, 0x1f600, 1, 5, 1234, -9, 7, 1, 0]);
            failing = true;
            assert.equal(imports.rresult(), 1);
            assert.equal(harness.borrow(0, imports.rown()), owned);
            assert.throws(() => imports.rborrow(), /borrowed resources cannot be lowered/);
            assert.throws(() => imports.rlent(harness.address(64)), /borrowed resources cannot be lowered/);
            """);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LiftsAliasesUnitPayloadsAndJoinedVariantSlots(WasmTarget target)
    {
        Run(target, [
            ("small", """{"type":"u16"}"""),
            ("status", """{"result":{"ok":null,"err":null}}"""),
            ("float-int", """{"variant":{"cases":[{"name":"a","type":"f32"},{"name":"b","type":"u32"}]}}"""),
            ("long-int", """{"variant":{"cases":[{"name":"a","type":"s64"},{"name":"b","type":"u32"}]}}"""),
            ("long-float", """{"variant":{"cases":[{"name":"a","type":"s64"},{"name":"b","type":"f32"}]}}"""),
            ("long-double", """{"variant":{"cases":[{"name":"a","type":"s64"},{"name":"b","type":"f64"}]}}"""),
            ("float-double", """{"variant":{"cases":[{"name":"a","type":"f32"},{"name":"b","type":"f64"}]}}"""),
        ], [
            Function("flat", [
                ("small", D(0)), ("status", D(1)), ("float-int", D(2)), ("long-int", D(3)),
                ("long-float", D(4)), ("long-double", D(5)), ("float-double", D(6)),
            ], null),
        ], """
            let received;
            const harness = await open({ "sample:raw/api@1.0.0": { flat(...values) { received = values; } } });
            const bits = new DataView(new ArrayBuffer(8));
            bits.setFloat32(0, 1.5, true);
            const single = bits.getInt32(0, true);
            bits.setFloat32(0, -0.5, true);
            const wideSingle = BigInt(bits.getUint32(0, true));
            bits.setFloat64(0, 2.25, true);
            const wideDouble = bits.getBigInt64(0, true);
            assert.equal(harness.imports.flat(1234, 1, 0, single, 1, 7n, 1, wideSingle, 1, wideDouble, 0, 0.75), undefined);
            assert.deepEqual(received, [
              1234,
              { tag: "err", val: undefined },
              { tag: "a", val: 1.5 },
              { tag: "b", val: 7 },
              { tag: "b", val: -0.5 },
              { tag: "b", val: 2.25 },
              { tag: "a", val: 0.75 },
            ]);
            """);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RoundTripsEveryKindThroughLinearMemory(WasmTarget target)
    {
        static string Names(string prefix, int count) =>
            string.Join(",", Enumerable.Range(0, count).Select(index => $$"""{"name":"{{prefix}}{{index}}"}"""));
        static string Flags(int count) => """{"flags":{"flags":[""" + Names("f", count) + "]}}";
        static string Cases(int count, string firstType) =>
            string.Join(",", Enumerable.Range(0, count).Select(index =>
                $$"""{"name":"case{{index}}","type":{{(index == 0 ? firstType : "null")}}}"""));
        Run(target, [
            ("file-handle", "\"resource\""),
            (null, """{"handle":{"own":0}}"""),
            (null, """{"handle":{"borrow":0}}"""),
            ("small", """{"type":"u16"}"""),
            ("point", """{"record":{"fields":[{"name":"x","type":"s32"},{"name":"y","type":"f64"}]}}"""),
            ("pair", """{"tuple":{"types":["u8","string"]}}"""),
            ("maybe", """{"option":"u8"}"""),
            ("nested", """{"option":6}"""),
            ("status", """{"result":{"ok":null,"err":"u8"}}"""),
            ("named", """{"result":{"ok":"string","err":null}}"""),
            ("shape", """{"variant":{"cases":[{"name":"circle","type":"f32"},{"name":"label","type":"string"},{"name":"empty","type":null}]}}"""),
            ("color", """{"enum":{"cases":[{"name":"red"},{"name":"green"},{"name":"blue"}]}}"""),
            ("tiny", """{"flags":{"flags":[{"name":"a"},{"name":"b"},{"name":"c"}]}}"""),
            ("short", Flags(12)),
            ("word", Flags(20)),
            ("wide", Flags(40)),
            ("bytes", """{"list":"u8"}"""),
            ("words", """{"list":"u16"}"""),
            ("names", """{"list":"string"}"""),
            ("wide-enum", """{"enum":{"cases":[""" + Names("case", 300) + "]}}"),
            ("wide-variant", """{"variant":{"cases":[""" + Cases(300, "\"u32\"") + "]}}"),
            ("everything", """
                {"record":{"fields":[
                  {"name":"flag","type":"bool"},{"name":"tiny-int","type":"s8"},{"name":"byte","type":"u8"},
                  {"name":"short-int","type":"s16"},{"name":"half","type":"u16"},{"name":"int","type":"s32"},
                  {"name":"word-value","type":"u32"},{"name":"long","type":"s64"},{"name":"ulong","type":"u64"},
                  {"name":"single","type":"f32"},{"name":"double","type":"f64"},{"name":"letter","type":"char"},
                  {"name":"small","type":3},{"name":"text","type":"string"},{"name":"bytes","type":16},
                  {"name":"words","type":17},{"name":"names","type":18},{"name":"point","type":4},
                  {"name":"pair","type":5},{"name":"maybe","type":6},{"name":"nested","type":7},
                  {"name":"status","type":8},{"name":"named","type":9},{"name":"shape","type":10},
                  {"name":"color","type":11},{"name":"tiny","type":12},{"name":"short","type":13},
                  {"name":"word","type":14},{"name":"wide","type":15},{"name":"handle","type":1},
                  {"name":"wide-enum","type":19},{"name":"wide-variant","type":20},
                  {"name":"title","type":23},{"name":"smalls","type":24}]}}
                """),
            ("borrows", """{"list":2}"""),
            ("label", """{"type":"string"}"""),
            ("halves", """{"list":3}"""),
        ], [
            Function("make", [], D(21)),
            Function("take", [("value", D(21))], null),
            Function("peek", [("handles", D(22))], P("u32")),
        ], """
            class FileHandle { constructor(name) { this.name = name; } }
            const flags = (count, set) => Object.fromEntries(
              Array.from({ length: count }, (_, index) => [`f${index}`, set.includes(index)]));
            let made;
            let received;
            let peeked;
            const harness = await open({ "sample:raw/api@1.0.0": {
              FileHandle,
              make() { return made; },
              take(value) { received = value; },
              peek(handles) { peeked = handles; return handles.length; },
            } });
            const { imports, address, wasm64 } = harness;
            // make lowers the value at 256; take lifts it back from there.
            const round = (value, lifted = value) => {
              made = value;
              assert.equal(imports.make(address(256)), undefined);
              assert.equal(imports.take(address(256)), undefined);
              const { handle, ...rest } = received;
              const { handle: expectedHandle, ...expected } = lifted;
              assert.equal(handle, expectedHandle);
              assert.deepEqual(rest, expected);
            };
            const first = {
              flag: true, tinyInt: -5, byte: 200, shortInt: -300, half: 60000, int: -70000, wordValue: 4000000000,
              long: -(2n ** 40n), ulong: 2n ** 63n, single: Math.fround(1.1), double: Math.PI, letter: "\u{1f600}",
              small: 1234, text: "héllo", bytes: new Uint8Array([1, 2, 3]), words: [1, 65535], names: ["a", "βγ"],
              point: { x: -3, y: 2.5 }, pair: [7, "q"], maybe: 9, nested: { tag: "some", val: 4 },
              status: { tag: "ok", val: undefined }, named: { tag: "ok", val: "n" }, shape: { tag: "label", val: "sq" },
              color: "blue", tiny: { a: true, b: false, c: true }, short: flags(12, [0, 11]), word: flags(20, [19]),
              wide: flags(40, [1, 39]), handle: new FileHandle("first"), wideEnum: "case299",
              wideVariant: { tag: "case0", val: 77 }, title: "t", smalls: [1, 2],
            };
            round(first);
            const second = {
              flag: false, tinyInt: 127, byte: 0, shortInt: 32767, half: 0, int: 2147483647, wordValue: 0,
              long: 9223372036854775807n, ulong: 0n, single: -0.5, double: -1e300, letter: "z",
              small: 65535, text: "", bytes: new Uint8Array(0), words: [], names: [],
              point: { x: 0, y: 0 }, pair: [0, ""], maybe: undefined, nested: { tag: "none" },
              status: { tag: "err", val: 3 }, named: { tag: "err", val: undefined }, shape: { tag: "empty" },
              color: "red", tiny: { a: false, b: false, c: false }, short: flags(12, []), word: flags(20, []),
              wide: flags(40, []), handle: new FileHandle("second"), wideEnum: "case0", wideVariant: { tag: "case299" },
              title: "", smalls: new Uint16Array([65535]),
            };
            // A numeric list may arrive as its typed array; it lifts as an array.
            round(second, { ...second, smalls: [65535] });
            const third = { ...second, nested: { tag: "some", val: undefined }, shape: { tag: "circle", val: 1.5 },
              handle: new FileHandle("third") };
            round(third, { ...third, smalls: [65535] });
            const one = harness.register(new FileHandle("one"));
            const two = harness.register(new FileHandle("two"));
            harness.view().setUint32(1024, one, true);
            harness.view().setUint32(1028, two, true);
            assert.equal(imports.peek(address(1024), wasm64 ? 2n : 2), 2);
            assert.deepEqual(peeked.map(handle => handle.name), ["one", "two"]);
            assert.equal(harness.borrow(0, one).name, "one");
            """);
    }

    private static WitTypeReference.Primitive P(string name) => new(name);

    private static WitTypeReference.Defined D(int id) => new(id);

    private static TestFunction Function(
        string name,
        ImmutableArray<(string Name, WitTypeReference Type)> parameters,
        WitTypeReference? result,
        WitFunctionKind? kind = null) => new(name, parameters, result, kind ?? new("freestanding"));

    private static void Run(
        WasmTarget target,
        ImmutableArray<(string? Name, string Source)> types,
        ImmutableArray<TestFunction> functions,
        string script,
        int heapStart = 4096)
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-raw-adapter-");
        try
        {
            var adapter = Path.Combine(directory.FullName, "adapter.mjs");
            File.WriteAllText(adapter, Write(target, types, functions));
            var harness = Path.Combine(AppContext.BaseDirectory, "Raw", "raw-adapter-harness.mjs");
            var driver = Path.Combine(directory.FullName, "driver.mjs");
            File.WriteAllText(driver, $$"""
                import assert from "node:assert/strict";
                import { openAdapter } from {{JsonSerializer.Serialize(new Uri(harness).AbsoluteUri)}};
                const open = providers => openAdapter({
                  adapterPath: process.argv[2],
                  repositoryRoot: process.argv[3],
                  target: {{JsonSerializer.Serialize(target == WasmTarget.Wasm64 ? "wasm64" : "wasm32")}},
                  providers,
                  heapStart: {{heapStart}},
                });
                {{script}}
                """);
            using var process = Process.Start(new ProcessStartInfo(
                Environment.GetEnvironmentVariable("NETWASM_NODE_PATH") ?? "node",
                [driver, adapter, RepositoryRoot()])
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            })!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"{output}\n{error.Result}");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string Write(
        WasmTarget target,
        ImmutableArray<(string? Name, string Source)> types,
        ImmutableArray<TestFunction> functions)
    {
        var document = new WitDocument(
            [],
            [new WitInterface(0, "api", "sample:raw@1.0.0", ImmutableDictionary<string, int>.Empty, [])],
            [],
            [.. types.Select((type, id) => TypeDefinition(id, type.Name, type.Source))],
            "{\"world\":\"execution\"}");
        var resolver = new WitCanonicalTypeResolver();
        var planner = new RawCanonicalFunctionLayoutPlanner(
            new RawCanonicalImportIdentityFormatter(),
            new CanonicalAbiSignaturePlanner(new CanonicalAbiTypeFlattener()),
            new CanonicalAbiMemoryLayoutPlanner());
        var layouts = ImmutableArray.CreateBuilder<RawWitImportLayout>();
        var imports = ImmutableDictionary.CreateBuilder<RawCanonicalImportIdentity, RawWitImportDeclaration>();
        foreach (var function in functions)
        {
            var canonical = new CanonicalAbiFunction(
                Interface,
                function.Name,
                default,
                [.. function.Parameters.Select(parameter =>
                    new CanonicalAbiParameter(parameter.Name, resolver.Resolve(document, parameter.Type)))],
                function.Result is null ? null : resolver.Resolve(document, function.Result));
            var declaration = new WitFunction(
                function.Name,
                [.. function.Parameters.Select(parameter => new WitParameter(parameter.Name, parameter.Type))],
                function.Result,
                function.Kind);
            var layout = new RawWitImportLayout.Callable(new(declaration, planner.Plan(canonical, target)));
            layouts.Add(layout);
            imports.Add(layout.Identity, new RawWitImportDeclaration.Callable(Interface, DeploymentInterface, declaration));
        }
        var catalog = new RawWitImportCatalog(
            document,
            new WitWorld(0, "execution", "sample:raw@1.0.0", [], []),
            target,
            imports.ToImmutable());
        var selection = new RawImportBindingSelection(catalog, [.. layouts.Select(layout => layout.Identity)], [], []);
        var plan = new RawWitBindingPlan(selection, layouts.ToImmutable());
        var expected = layouts.Select(layout => new RawCoreFunctionImportSignature(
            layout.Identity,
            [.. layout.Signature.Parameters.Select(value => Project(value, target))],
            layout.Signature.Result == CliValueKind.Void ? [] : [Project(layout.Signature.Result, target)]))
            .ToImmutableArray();
        var bytes = new RawAdapterWriter(new WitInterfaceSpecifierFormatter()).Write(new(plan, expected, expected));
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static WitTypeDefinition TypeDefinition(int id, string? name, string source)
    {
        using var json = JsonDocument.Parse(source);
        return new(id, name, json.RootElement.Clone(), 0);
    }

    private static RawCoreValueType Project(CliValueKind value, WasmTarget target) => value switch
    {
        CliValueKind.I4 or CliValueKind.ValueType => RawCoreValueType.I32,
        CliValueKind.I8 => RawCoreValueType.I64,
        CliValueKind.F4 => RawCoreValueType.F32,
        CliValueKind.F8 => RawCoreValueType.F64,
        _ => target == WasmTarget.Wasm64 ? RawCoreValueType.I64 : RawCoreValueType.I32,
    };

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NetWasm.slnx"))) return directory.FullName;
        }
        throw new InvalidOperationException("The repository root was not found.");
    }

    private sealed record TestFunction(
        string Name,
        ImmutableArray<(string Name, WitTypeReference Type)> Parameters,
        WitTypeReference? Result,
        WitFunctionKind Kind);
}
