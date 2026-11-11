import { open } from "./browser/BrowserHosts.mjs";
import { createWorker } from "./browser/BrowserHosts.worker-client.mjs";

function check(condition, label) {
  if (!condition) throw new Error(label);
}

const result = document.querySelector("#result");
const notifications = { page: [], worker: [] };
let page;
let worker;
try {
  page = await open({ onNotification: value => notifications.page.push(value.arguments[0]) });
  worker = await createWorker({ onNotification: value => notifications.worker.push(value.arguments[0]) });
  // The page returns values directly; the worker returns Promises of the same values.
  const pageResults = [page.twice(21), page.label(7), page.sum(Int32Array.of(1, 2, 39)), await page.later("x")];
  const workerResults = [await worker.twice(21), await worker.label(7),
    await worker.sum(Int32Array.of(1, 2, 39)), await worker.later("x")];
  check(JSON.stringify(pageResults.map(String)) === JSON.stringify(["42", "#7", "42", "5"]),
    `Page results differ: ${pageResults}`);
  check(JSON.stringify(workerResults.map(String)) === JSON.stringify(pageResults.map(String)),
    `Worker results differ: ${workerResults}`);
  await Promise.resolve();
  check(notifications.page.join() === "7" && notifications.worker.join() === "7",
    "Notifications differ between hosts.");
  // Issue #216: each page call collected, twice with the event loop's turn after it, once the
  // module had finalization. Calls that allocate nothing must leave collection to allocation.
  check(page.finalizes() === 1, "The module's finalizer did not run.");
  const collections = page.collections();
  for (let i = 0; i < 10_000; i++) page.twice(i);
  const collected = page.collections() - collections;
  check(collected < 100, `10,000 twice() calls collected ${collected} times.`);
  // Report the cost of one int(int) call on each host (median of five runs).
  const median = values => values.sort((a, b) => a - b)[2];
  const pageRuns = [];
  const workerRuns = [];
  for (let run = 0; run < 5; run++) {
    let start = performance.now();
    for (let i = 0; i < 20_000; i++) page.twice(i);
    pageRuns.push((performance.now() - start) * 1000 / 20_000);
    start = performance.now();
    for (let i = 0; i < 500; i++) await worker.twice(i);
    workerRuns.push((performance.now() - start) * 1000 / 500);
  }
  document.body.dataset.metrics = JSON.stringify({
    pageMicrosecondsPerCall: median(pageRuns),
    workerMicrosecondsPerCall: median(workerRuns),
  });
  await page.close();
  await worker.dispose();
  result.textContent = "passed";
  document.body.dataset.state = "passed";
} catch (error) {
  await page?.close().catch(() => {});
  worker?.terminate();
  result.textContent = String(error?.stack ?? error);
  document.body.dataset.state = "failed";
}
