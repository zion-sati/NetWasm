import assert from "node:assert/strict";
import test from "node:test";

import { createLocalDirectoryGrants, guestPath } from "./local-directory-grants.mjs";

function request(grants = {}, environment = []) {
  return Object.freeze({
    schemaVersion: 1,
    environment: Object.freeze(environment),
    grants: Object.freeze({
      clocks: Object.freeze([]),
      environment: Object.freeze(environment.map(variable => variable.name)),
      network: "denyAll",
      preopens: Object.freeze([]),
      randomness: false,
      temporaryDirectory: false,
      workingDirectory: false,
      ...grants,
    }),
  });
}

function grants(overrides = {}) {
  const removed = [];
  let made = 0;
  const grant = createLocalDirectoryGrants({
    makeTemporaryDirectory: async () => `/host/tmp/netwasm-${++made}`,
    platform: "linux",
    removeDirectory: async path => { removed.push(path); },
    workingDirectory: () => "/work/project",
    ...overrides,
  });
  return { grant, removed };
}

test("the program starts in the working directory, mounted only when granted", async () => {
  const { grant, removed } = grants();
  const plain = await grant(request());
  assert.equal(plain.initialDirectory, "/work/project");
  assert.deepEqual(plain.request.grants.preopens, []);
  await plain.release();
  assert.deepEqual(removed, []);

  const granted = await grant(request({ workingDirectory: true }));
  assert.deepEqual(granted.request.grants.preopens, [
    { hostPath: "/work/project", guestPath: "/work/project", access: "readWrite" },
  ]);
  assert.equal(granted.request.grants.workingDirectory, false);
  assert.equal(Object.isFrozen(granted.request.grants.preopens), true);
});

test("an explicit preopen at the same guest path wins", async () => {
  const { grant } = grants();
  const explicit = Object.freeze({ hostPath: "/elsewhere", guestPath: "/work/project", access: "readOnly" });
  const tmp = Object.freeze({ hostPath: "/mine", guestPath: "/tmp", access: "readOnly" });
  const result = await grant(request({
    preopens: Object.freeze([explicit, tmp]),
    temporaryDirectory: true,
    workingDirectory: true,
  }));
  assert.deepEqual(result.request.grants.preopens, [explicit, tmp]);
  assert.deepEqual(result.request.environment, []);
});

test("a temporary directory is made for the run, seen at /tmp and removed after it", async () => {
  const { grant, removed } = grants();
  const result = await grant(request({ temporaryDirectory: true }, [
    Object.freeze({ name: "TMPDIR", value: "/var/folders/host/" }),
    Object.freeze({ name: "A", value: "1" }),
    Object.freeze({ name: "Z", value: "2" }),
  ]));
  assert.deepEqual(result.request.grants.preopens, [
    { hostPath: "/host/tmp/netwasm-1", guestPath: "/tmp", access: "readWrite" },
  ]);
  assert.deepEqual(result.request.environment, [
    { name: "A", value: "1" }, { name: "TMPDIR", value: "/tmp/" }, { name: "Z", value: "2" },
  ]);
  assert.deepEqual(result.request.grants.environment, ["A", "TMPDIR", "Z"]);
  assert.equal(result.request.grants.temporaryDirectory, false);
  await result.release();
  assert.deepEqual(removed, ["/host/tmp/netwasm-1"]);
});

test("Windows drives are seen as /c/...", async () => {
  assert.equal(guestPath("C:\\Work\\project\\", "win32"), "/c/Work/project");
  assert.equal(guestPath("D:\\", "win32"), "/d");
  assert.equal(guestPath("e:", "win32"), "/e");
  assert.throws(() => guestPath("\\\\server\\share", "win32"), /not on a drive/u);
  assert.equal(guestPath("/", "linux"), "/");
  assert.equal(guestPath("/srv/app/", "darwin"), "/srv/app");
  const { grant } = grants({ platform: "win32", workingDirectory: () => "C:\\work" });
  assert.equal((await grant(request())).initialDirectory, "/c/work");
});

test("the options are explicit", () => {
  assert.throws(() => createLocalDirectoryGrants(null), /options is invalid/u);
  assert.throws(() => createLocalDirectoryGrants({}), /shape is invalid/u);
  for (const name of ["makeTemporaryDirectory", "removeDirectory", "workingDirectory"]) {
    assert.throws(() => grants({ [name]: null }), new RegExp(`'${name}' action is required`, "u"));
  }
  assert.throws(() => grants({ platform: 1 }), /platform is required/u);
});
