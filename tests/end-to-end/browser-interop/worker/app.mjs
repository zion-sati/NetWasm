import { createWorker } from "./browser/BrowserWorker.worker-client.mjs";

function check(condition, label) {
  if (!condition) throw new Error(label);
}

async function rejection(call) {
  try { await call(); } catch (error) { return error; }
  throw new Error("Expected rejected operation.");
}

const result = document.querySelector("#result");
const debug = new URL(location.href).searchParams.get("profile") === "Debug";
function checkPayload(error, message) {
  check(error.message === (message ?? "System.FormatException"), "Managed failure message was lost.");
  check(error.managed?.message === message, "Managed payload message was lost.");
  check(error.managed?.typeId > 0, "Managed exception type was lost.");
  check(error.managed?.typeName === "System.FormatException", "Managed exception type was lost.");
  if (debug) {
    check(error.managed?.stackTrace?.includes("WorkerExports"), "Managed stack frames were lost.");
    check(/Program\.cs:line \d+/.test(error.managed.stackTrace), "Managed source location was lost.");
  }
}
let first;
let second;
try {
  first = await createWorker({ onNotification({ operation, arguments: values }) {
    check(operation === "progress", "Unexpected notification.");
    document.querySelector("#progress").textContent = String(values[0]);
  } });
  second = await createWorker();
  const runtime = await (await fetch(new URL("./browser/BrowserWorker.worker.mjs", import.meta.url))).text();
  check(!runtime.includes("netwasm-fixture-optional-feature"), "An unused declared module was bundled.");
  check(await first.add(10) === 10 && await second.add(1) === 1, "Instances share state.");
  check(await first.task(41) === 42, "Task result mismatch.");
  check(document.querySelector("#progress").textContent === "41", "Notification did not reach page.");
  check(await first.valueTask(40) === 42, "ValueTask result mismatch.");
  const error = await rejection(() => first.fail());
  document.body.dataset.testCase = "fail";
  checkPayload(error, "worker-probe");
  for (const operation of ["failTask", "failValueTask", "failValueTaskResult"]) {
    for (const [kind, supplied] of [[0, ""], [1, "Unicode Ω 😀"], [2, "call-specific"], [5, null]]) {
      document.body.dataset.testCase = `${operation}-${kind}`;
      // Transport preserves the stored nullable message, not the managed
      // Message getter's synthesized default. Null uses the mapped type name
      // for the public error; an explicitly empty message stays empty.
      checkPayload(await rejection(() => first[operation](kind)), supplied);
    }
  }
  const overlapping = await Promise.all([
    rejection(() => first.failTask(3)),
    rejection(() => second.failValueTask(4)),
  ]);
  checkPayload(overlapping[0], "first instance");
  checkPayload(overlapping[1], "second instance");
  check(await first.add(32) === 42, "Failed export poisoned the session.");
  document.body.dataset.testCase = "concurrent";
  const waiting = first.waitForRelease();
  await first.release(41);
  check(await waiting === 42, "A call could not start while another awaited.");
  const counting = first.countUntilStopped();
  await first.stop();
  check(await counting >= 1, "A running job could not be stopped through another export.");
  document.body.dataset.testCase = "async-results";
  check(await first.describe("x") === "worker:x", "Task<string> worker result failed.");
  const halves = await first.halves(Int32Array.of(2, 5));
  check(halves instanceof Float64Array && halves[0] === 1 && halves[1] === 2.5,
    "ValueTask<double[]> worker result failed.");
  const draining = first.task(41);
  await first.dispose();
  check(await draining === 42, "Graceful disposal did not drain the pending call.");
  await rejection(() => first.add(1));
  check(await second.add(41) === 42, "Disposal affected another instance.");
  document.body.dataset.testCase = "abandon";
  const abandon = new AbortController();
  const abandoned = second.pending({ signal: abandon.signal });
  abandon.abort();
  check((await rejection(() => abandoned)).name === "AbortError", "Aborting a call did not reject it.");
  check(await second.add(0) === 42, "An abandoned call held back later calls.");
  const pending = second.pending().then(() => false, () => true);
  second.terminate();
  check(await pending, "Termination did not reject the pending call.");
  await rejection(() => second.add(1));
  await second.dispose();
  result.textContent = "passed";
  document.body.dataset.state = "passed";
} catch (error) {
  first?.terminate();
  second?.terminate();
  result.textContent = String(error?.stack ?? error);
  document.body.dataset.state = "failed";
}
