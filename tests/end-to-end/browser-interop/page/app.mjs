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
let current;
// Supplied at open() beside the bundled app.bundled module.
const host = {
  show(text) { document.querySelector("#shown").textContent = text; },
  async shout(text) { await Promise.resolve(); return text.toUpperCase(); },
  title(element) { return element.title; },
  listen(callback) {
    listener = callback;
    return { dispose() { listener = null; } };
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

  document.body.dataset.testCase = "callback";
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

  result.textContent = "passed";
  document.body.dataset.state = "passed";
} catch (error) {
  await first?.close().catch(() => {});
  await second?.close().catch(() => {});
  result.textContent = String(error?.stack ?? error);
  document.body.dataset.state = "failed";
}
