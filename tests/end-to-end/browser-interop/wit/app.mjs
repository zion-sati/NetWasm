// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

import { createWorker } from "./browser/App.worker-client.mjs";

const output = document.querySelector("#result");
let worker;
const notifications = [];
try {
  worker = await createWorker({
    onNotification({ operation, arguments: values }) {
      if (operation !== "progress") throw new Error("Unexpected notification.");
      notifications.push(values);
    },
  });
  const run = worker["netwasm:worker-template/work@1.0.0/run"];
  for (const total of [5, 3]) {
    notifications.length = 0;
    const result = await run(total);
    if (result !== total || notifications.length !== total
        || notifications.some((values, index) => values[0] !== index + 1 || values[1] !== total))
      throw new Error("WIT result/notification contract failed.");
  }
  await worker.dispose();
  let rejected = false;
  try { await run(1); } catch { rejected = true; }
  if (!rejected) throw new Error("Disposed WIT worker accepted a call.");
  document.body.dataset.state = "passed";
  output.value = "WIT worker passed";
} catch (error) {
  document.body.dataset.state = "failed";
  output.value = String(error);
} finally {
  if (worker) await worker.dispose();
}
