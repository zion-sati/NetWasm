import { open } from "./browser/BrowserPage.mjs";

function check(condition, label) {
  if (!condition) throw new Error(label);
}

async function failure(call) {
  try { await call(); } catch (error) { return error; }
  throw new Error("Expected a failed call.");
}

const result = document.querySelector("#result");
const lines = [];
const errors = [];
let listener = null;
let labelled = null;
let shifted = null;
let picked = null;
let inspected = null;
let current;
// Supplied at open() beside the bundled app.bundled module.
const host = {
  show(text) { document.querySelector("#shown").textContent = text; },
  async shout(text) { await Promise.resolve(); return text.toUpperCase(); },
  never() { return new Promise(() => {}); },
  title(element) { return element.title; },
  listen(callback) {
    listener = callback;
    return { dispose() { listener = null; } };
  },
  label(callback) {
    labelled = callback("a", "b", document.querySelector("#probe"));
    return { dispose() {} };
  },
  reenter(depth) { return current.nest(depth); },
  fill(pixels, value) {
    check(pixels instanceof Uint8Array, "A Span<byte> is not a Uint8Array.");
    pixels.fill(value);
    return pixels.length;
  },
  sum(values) {
    check(values instanceof Float64Array, "A ReadOnlySpan<double> is not a Float64Array.");
    return values.reduce((total, value) => total + value, 0);
  },
  count(values) {
    check(values instanceof Int32Array, "A Span<int> is not an Int32Array.");
    return values.length;
  },
  nextDay(value) {
    check(value instanceof Date, "A DateTime argument is not a Date.");
    return new Date(value.getTime() + 86_400_000);
  },
  nextHour(value, label) {
    check(value instanceof Date && label === "h", "A DateTimeOffset argument is not a Date.");
    return new Date(value.getTime() + 3_600_000);
  },
  async dateLater(value) { await Promise.resolve(); return new Date(value.getTime() + 1000); },
  shift(callback) {
    shifted = callback(new Date(5000));
    return { dispose() {} };
  },
  half(value) { return value === null ? null : value / 2; },
  async maybeLater(present) { await Promise.resolve(); return present ? 7n : null; },
  pick(callback) {
    picked = [callback(5, new Date(0)), callback(null, null), callback(1, null)];
    return { dispose() {} };
  },
  nothing() { return null; },
  echo(value) { return value; },
  words(values) { return values === null ? null : values.map(value => value === null ? null : value.toUpperCase()); },
  raise(message) { return new TypeError(message); },
  async collect(elements) { await Promise.resolve(); return elements === null ? null : [...elements, "extra"]; },
  inspect(callback) {
    inspected = [callback(5, null), callback("x", ["a", null])];
    return { dispose() {} };
  },
  bump(value) {
    check(typeof value === "number", "A long marked JSType.Number is not a number.");
    return value + 1;
  },
};

let first;
let second;
try {
  first = await open({
    environment: { MODE: "page" },
    modules: { "app.host": host },
    platform: { now: () => 86_400_000 },
    stdout: line => lines.push(line),
    stderr: line => errors.push(line),
  });
  second = await open({ modules: { "app.host": host } });
  current = first;

  document.body.dataset.testCase = "optional-module";
  const runtime = await (await fetch(new URL("./browser/netwasm.page.mjs", import.meta.url))).text();
  check(!runtime.includes("netwasm-fixture-optional-feature"), "An unused declared module was bundled.");

  document.body.dataset.testCase = "sync";
  check(first.add(40) === 40 && second.add(1) === 1, "Instances share state.");
  check(first.add(2) === 42, "Synchronous export result mismatch.");
  check(first.echo("hi") === "bundled:hi", "String export or bundled import failed.");
  check(document.querySelector("#shown").textContent === "hi", "Supplied import did not reach the DOM.");
  check(first.echo(null) === null, "Null string did not round-trip.");

  document.body.dataset.testCase = "typed-array";
  const scaled = first.scale(Float64Array.of(1.5, -2), 2);
  check(scaled instanceof Float64Array && scaled[0] === 3 && scaled[1] === -4, "Typed array round trip failed.");

  document.body.dataset.testCase = "span-view";
  check(first.fillPixels(1024, 9) === 1024 + 4 + 8, "Span view writes did not reach C#.");
  check(first.sumMiddle(Float64Array.of(100, 1.5, 2.5, 100)) === 4, "ReadOnlySpan slice view failed.");
  check(first.countInts() === 29, "Span<int> views or the misaligned-span failure failed.");

  document.body.dataset.testCase = "jsobject";
  const probe = document.querySelector("#probe");
  check(first.describe(probe) === "probe title", "JSObject argument failed.");
  check(first.same(probe) === probe && first.same(null) === null, "JSObject identity was lost.");

  document.body.dataset.testCase = "async";
  check(await first.later("ok") === 1002, "Task export with an async string import failed.");
  check(await first.shoutLater("ok") === "OK!", "Task<string> export result failed.");
  const scaledLater = await first.scaleLater(Float64Array.of(1.5, -2));
  check(scaledLater instanceof Float64Array && scaledLater[0] === 3 && scaledLater[1] === -4,
    "ValueTask<double[]> export result failed.");
  check(await first.scaleLater(null) === null, "A null async array result did not round-trip.");
  check(await first.sameLater(probe) === probe && await first.sameLater(null) === null,
    "Task<JSObject> export result lost the object.");

  document.body.dataset.testCase = "dates";
  const at = milliseconds => new Date(milliseconds);
  check(first.addHours(at(0), 2).getTime() === 7_200_000, "A DateTime export did not round-trip a Date.");
  check(first.tomorrow(at(1000)).getTime() === 86_401_000, "A DateTime import did not round-trip a Date.");
  check(first.hourLater("h", at(0)) === "3600000h", "A DateTimeOffset import beside a string failed.");
  check(first.utcKind(at(0)), "A Date did not arrive as a UTC DateTime.");
  check(first.localNoon().getTime() === 129_600_000,
    "A local DateTime did not cross as its ticks in UTC, in whole milliseconds.");
  check((await first.laterDate(at(0))).getTime() === 1001,
    "A Task<DateTime> export with an async DateTime import failed.");
  check((await first.offsetLater(at(0))).getTime() === 86_400_000, "A ValueTask<DateTimeOffset> export failed.");
  check(first.shiftThroughCallback().getTime() === 5000 && shifted?.getTime() === 6000,
    "A callback taking a DateTime and returning a DateTimeOffset failed.");
  check((await failure(() => first.addHours(at(8.64e15), 0))).name === "NetWasmManagedError",
    "A Date past DateTime's range was not refused.");
  check((await failure(() => first.addHours(0, 0))).name === "NetWasmHostError", "A number was taken as a Date.");
  check(first.add(0) === 42, "A refused Date poisoned the instance.");

  document.body.dataset.testCase = "nullable";
  check(first.maybeAdd(40, 2) === 42 && first.maybeAdd(null, 2) === null, "An int? export did not round-trip null.");
  check(first.halfOf(5) === 2.5 && first.halfOf(null) === null, "A double? import did not round-trip null.");
  check(first.maybeDay(at(0)).getTime() === 86_400_000 && first.maybeDay(null) === null,
    "A DateTime? export did not round-trip null.");
  check(await first.maybeLaterSum(true) === 8n && await first.maybeLaterSum(false) === null,
    "A Task<long?> export with an async long? import failed.");
  check((await first.maybeOffsetLater(at(5))).getTime() === 5 && await first.maybeOffsetLater(null) === null,
    "A ValueTask<DateTimeOffset?> export failed.");
  check(first.pickThroughCallback() === 3 && picked[0] === true && picked[1] === null && picked[2] === false,
    "A callback taking int? and DateTime? and returning bool? failed.");
  check(first.add(undefined) === 42 && first.nothingAsZero() === 0, "A null did not become a plain scalar's default.");

  document.body.dataset.testCase = "objects";
  check(first.describeValue(null) === "null" && first.describeValue(true) === "bool:True"
    && first.describeValue(2.5) === "double:2.5" && first.describeValue(7n) === "long:7"
    && first.describeValue("hi") === "string:hi" && first.describeValue(new Date(Date.UTC(2001, 0, 1))) === "date:2001"
    && first.describeValue(new RangeError("bad")) === "error:bad" && first.describeValue([1, "a", null]) === "array:3"
    && first.describeValue(Uint8Array.of(1, 2, 3)) === "bytes:3" && first.describeValue(probe) === "object",
    "An object parameter lost its JavaScript type.");
  const values = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9].map(kind => first.valueOf(kind));
  check(values[0] === null && values[1] === true && values[2] === 2.5 && values[3] === "text"
    && values[4] instanceof Date && values[4].getTime() === Date.UTC(2000, 0, 1)
    && Array.isArray(values[5]) && values[5][0] === 1 && values[5][1] === "two" && values[5][2] === null
    && values[6] instanceof Uint8Array && values[6].length === 2
    && values[7] instanceof Error && values[7].name === "ManagedError" && values[7].message === "managed"
    && typeof values[8] === "object" && values[9] === 42, "An object result lost its managed type.");
  check(first.sameManaged(values[8]) && !first.sameManaged({}), "A managed object did not come back as itself.");
  check((await failure(() => first.valueOf(10))).name === "NetWasmManagedError", "A long past 2^53 was not refused.");
  check(first.echoThroughHost(probe) === probe && first.echoThroughHost("s") === "s" && first.echoThroughHost(null) === null,
    "An object import did not round-trip its value.");
  check(first.upperWords(["a", null, "b"]).join() === "A,,B" && first.upperWords(null) === null,
    "A string[] did not round-trip.");
  check((await failure(() => first.upperWords([1]))).name === "NetWasmHostError", "A number was taken as a string.");
  check(first.wrapError("boom") === "js:boom", "A JavaScript Error did not become a JSException.");
  const thrownError = new RangeError("r");
  check(first.errorOf(thrownError) === thrownError && first.errorOf(null) === null,
    "A JSException did not give its Error back.");
  const managedError = first.managedError();
  check(managedError instanceof Error && managedError.name === "ManagedError" && managedError.message === "managed-format",
    "A managed exception did not cross as an Error.");
  check(await first.collectLater([probe, null]) === 3, "A Task<object[]> import with a JSObject[] argument failed.");
  check(first.elementsOf([probe, null])[0] === probe, "A JSObject[] did not keep its objects.");
  check(first.inspectThroughCallback() === 2 && inspected[0] === 5 && inspected[1] === 2,
    "A callback taking object and string[] failed.");

  document.body.dataset.testCase = "marshal-as";
  check(first.bumpNumber(41) === 42, "A long marked JSType.Number did not cross as a number.");
  check((await failure(() => first.bumpNumber(0.5))).name === "NetWasmHostError", "A fraction was taken as an Int52.");
  check((await failure(() => first.bigAsNumber())).name === "NetWasmHostError", "A long past 2^53 crossed as a number.");
  check(await first.bumpLater(1) === 2 && await first.bumpLater(null) === 0,
    "A Task<long> marked JSType.Promise<JSType.Number> failed.");

  document.body.dataset.testCase = "cancellation";
  check(await first.waitOrTimeOut(10) === -1, "WaitAsync did not stop waiting for a JavaScript promise.");
  const abandon = new AbortController();
  const abandoned = first.pendingLater({ signal: abandon.signal });
  abandon.abort();
  check((await failure(() => abandoned)).name === "AbortError", "Aborting an async export call did not reject it.");

  document.body.dataset.testCase = "callback";
  check(first.labelThroughCallback() === "a/b/probe title" && labelled === "a/b/probe title",
    "A callback with two strings and a JSObject did not return its string.");
  first.startListening();
  listener(5);
  listener(37);
  check(first.heard() === 42, "Callback did not reach managed code.");
  first.stopListening();
  check(listener === null, "Subscription was not disposed.");

  document.body.dataset.testCase = "reentrancy";
  check(first.nest(3) === 3, "Re-entrant export calls failed.");
  check(first.nest(31) === 31, "Re-entrant calls at the depth limit failed.");
  check(first.nest(32) === -1, "Re-entry past the depth limit was not refused.");

  document.body.dataset.testCase = "options";
  check(first.variable("MODE") === "page", "Environment override was lost.");
  // A long crosses as a bigint.
  check(first.now() === 86_400_000n, "Platform clock hook was ignored.");
  first.print("first line");
  first.print("second line");
  check(lines.join("|") === "first line|second line", "Line output was lost.");

  document.body.dataset.testCase = "failure";
  const thrown = await failure(() => first.fail());
  check(thrown.name === "NetWasmManagedError", "Synchronous failure type was lost.");
  check(errors.some(line => line.includes("page-probe")), "The managed exception was not reported on stderr.");
  const rejected = await failure(() => first.failLater());
  check(rejected.name === "NetWasmManagedError", "Asynchronous failure type was lost.");
  check(first.add(0) === 42, "A failed call poisoned the instance.");

  document.body.dataset.testCase = "close";
  await first.close();
  await first.failure;
  const closed = await failure(() => first.add(1));
  check(closed.name === "NetWasmHostError", "A call after close() was not refused.");
  check(second.add(1) === 2, "Closing one instance affected another.");
  await second.close();

  // Issue #128: a page publishes the worker project it references into its
  // own deployment, and both load one shared runtime instead of two copies.
  document.body.dataset.testCase = "worker-reference";
  const { createWorker } = await import("./browser/workers/PageWorker/PageWorker.worker-client.mjs");
  const worker = await createWorker();
  check(await worker.square(7) === 49, "A referenced worker did not answer.");
  check((await worker.dayAfter(new Date(0))).getTime() === 86_400_000, "A Date did not cross into a worker.");
  check(await worker.maybeSquare(3) === 9 && await worker.maybeSquare(null) === null,
    "An int? did not cross into a worker.");
  check(await worker.countWords(["a", null], "x") === 3, "A string[] and an object did not cross into a worker.");
  await worker.dispose();
  const text = async path => (await fetch(new URL(path, import.meta.url))).text();
  const runtimeImports = source => [...source.matchAll(/["'](\.\.?\/(?:\.\.\/)*netwasm\.runtime\.[\w.-]+\.mjs)["']/g)]
    .map(match => match[1]);
  const pageRuntime = runtimeImports(await text("./browser/netwasm.page.mjs"));
  const workerRuntime = runtimeImports(await text("./browser/workers/PageWorker/PageWorker.worker.mjs"));
  const clientRuntime = runtimeImports(await text("./browser/workers/PageWorker/PageWorker.worker-client.mjs"));
  check(pageRuntime.length !== 0 && pageRuntime.every(path => path.startsWith("./")),
    "The page does not import the shared runtime beside it.");
  check(workerRuntime.length !== 0 && [...workerRuntime, ...clientRuntime].every(path => path.startsWith("../../")),
    "The referenced worker does not import the page's runtime.");
  const chunks = async path => runtimeImports(await text("./browser/" + path.replace(/^(\.\.\/)+|^\.\//, "")));
  const pageChunks = new Set((await Promise.all(pageRuntime.map(chunks))).flat());
  const workerChunks = (await Promise.all(workerRuntime.map(chunks))).flat();
  check(workerChunks.some(chunk => pageChunks.has(chunk)), "The page and its worker share no runtime code.");

  result.textContent = "passed";
  document.body.dataset.state = "passed";
} catch (error) {
  await first?.close().catch(() => {});
  await second?.close().catch(() => {});
  result.textContent = String(error?.stack ?? error);
  document.body.dataset.state = "failed";
}
