import { createWorker } from "./browser/NetWasmProject.worker-client.mjs";

const output = document.querySelector("#result");
const worker = await createWorker({
  onNotification({ operation, arguments: values }) {
    if (operation === "progress") output.value = `Progress: ${values[0]}/${values[1]}`;
  },
});

try {
  const result = await worker.run(5);
  output.value = `Completed: ${result}`;
} finally {
  await worker.dispose();
}
