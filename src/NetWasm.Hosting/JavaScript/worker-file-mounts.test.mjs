import assert from "node:assert/strict";
import test from "node:test";

import { createWorkerFileMounts } from "./worker-file-mounts.mjs";

function session(name, calls, failing = null) {
  return {
    async files(action, ...arguments_) {
      calls.push([name, action, ...arguments_]);
      if (action === failing) throw new Error(`${name} cannot ${action}`);
    },
  };
}

test("page mounts reach every worker, present and future", async () => {
  const calls = [];
  const mounts = createWorkerFileMounts();
  const first = session("first", calls);
  const detachFirst = await mounts.attach(first);
  await mounts.mount("/in/a", "fileA");
  const second = session("second", calls);
  await mounts.attach(second);
  assert.deepEqual(calls, [["first", "mount", "/in/a", "fileA"], ["second", "mount", "/in/a", "fileA"]]);

  calls.length = 0;
  await mounts.sync("/in/a");
  detachFirst();
  await mounts.unmount("/in/a");
  assert.deepEqual(calls, [["first", "sync", "/in/a"], ["second", "sync", "/in/a"], ["second", "unmount", "/in/a"]]);
  await assert.rejects(mounts.unmount("/in/a"), /not mounted/);
  await assert.rejects(mounts.sync("/in/a"), /not mounted/);
});

test("a mount a worker refuses is not kept", async () => {
  const calls = [];
  const mounts = createWorkerFileMounts();
  await mounts.attach(session("refusing", calls, "mount"));
  await assert.rejects(mounts.mount("/in/b", "fileB"), /refusing cannot mount/);
  await assert.rejects(mounts.sync("/in/b"), /not mounted/);
  await mounts.mount("/in/c", "fileC").catch(() => {});
  await assert.rejects(mounts.mount("/in/c", "again"), /refusing cannot mount|already mounted/);
});

test("a path is mounted once", async () => {
  const mounts = createWorkerFileMounts();
  await mounts.mount("/once", "file");
  await assert.rejects(mounts.mount("/once", "file"), /already mounted/);
});
