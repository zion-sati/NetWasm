import assert from "node:assert/strict";
import test from "node:test";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import {
  linkedCorpusCliDependencies,
  linkedCorpusProgramDependencies,
  runLinkedCorpusCli,
  runLinkedCorpusMain,
} from "./linked-corpus-runner.mjs";

test("CLI reads, runs and writes exactly one response in order", async () => {
  const request = { id: 1 };
  const response = { id: 2 };
  const calls = [];
  await runLinkedCorpusCli(["request", "response"], {
    async readRequest(path) { calls.push(["read", path]); return request; },
    async run(value) { calls.push(["run", value]); return response; },
    async writeResponse(path, value) { calls.push(["write", path, value]); },
  });
  assert.deepEqual(calls, [
    ["read", "request"], ["run", request], ["write", "response", response],
  ]);
});

test("CLI rejects invalid arguments before dependencies", async () => {
  for (const args of [null, [], ["one"], ["", "two"], ["one", null], ["one", "two", "three"]]) {
    let called = false;
    await assert.rejects(runLinkedCorpusCli(args, {
      readRequest: async () => { called = true; },
      run: async () => { called = true; },
      writeResponse: async () => { called = true; },
    }), TypeError);
    assert.equal(called, false);
  }
});

test("CLI preserves the first dependency failure and stops", async () => {
  const cause = new Error("sentinel");
  for (const failedStage of ["read", "run", "write"]) {
    const calls = [];
    await assert.rejects(runLinkedCorpusCli(["request", "response"], {
      async readRequest() { calls.push("read"); if (failedStage === "read") throw cause; return {}; },
      async run() { calls.push("run"); if (failedStage === "run") throw cause; return {}; },
      async writeResponse() { calls.push("write"); if (failedStage === "write") throw cause; },
    }), error => error === cause);
    assert.equal(calls.at(-1), failedStage);
    assert.equal(calls.length, ["read", "run", "write"].indexOf(failedStage) + 1);
  }
});

test("default filesystem adapters preserve JSON and binary bytes", async () => {
  const directory = await mkdtemp(join(tmpdir(), "netwasm-linked-cli-"));
  try {
    const requestPath = join(directory, "request.json");
    const responsePath = join(directory, "response.json");
    const binaryPath = join(directory, "module.wasm");
    await writeFile(requestPath, JSON.stringify({ id: 1 }));
    await writeFile(binaryPath, new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0]));
    await runLinkedCorpusCli([requestPath, responsePath], {
      run: async request => ({ id: request.id + 1 }),
    });
    assert.deepEqual(JSON.parse(await readFile(responsePath, "utf8")), { id: 2 });
    assert.deepEqual(Array.from(await linkedCorpusProgramDependencies.readBinary(binaryPath)),
      [0, 97, 115, 109, 1, 0, 0, 0]);
    assert.deepEqual(await linkedCorpusProgramDependencies.readText(requestPath),
      JSON.stringify({ id: 1 }));
    const module = await linkedCorpusProgramDependencies.compileModule(
      await linkedCorpusProgramDependencies.readBinary(binaryPath));
    assert.deepEqual(linkedCorpusProgramDependencies.listImports(module), []);
    assert.equal(linkedCorpusProgramDependencies.hash(new Uint8Array()),
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
  } finally {
    await rm(directory, { recursive: true });
  }
});

test("main-module adapter runs only the matching executable", async () => {
  const calls = [];
  const path = join(tmpdir(), "linked-corpus-runner.mjs");
  const request = { moduleUrl: pathToFileURL(path).href, executablePath: path,
    args: ["request", "response"], run: async args => calls.push(args) };
  await runLinkedCorpusMain(request);
  await runLinkedCorpusMain({ ...request, moduleUrl: "file:///different.mjs" });
  await runLinkedCorpusMain({ ...request, executablePath: "" });
  assert.deepEqual(calls, [["request", "response"]]);
});

test("runtime composition supplies isolated cancellable reactor wakes for both widths", async () => {
  for (const target of ["wasm32", "wasm64"]) {
    const prefix = target === "wasm64" ? "cm64p2" : "cm32p2";
    const calls = [];
    const completion = Promise.withResolvers();
    const modules = linkedCorpusProgramDependencies.createRuntimeImports(
      target, () => { throw new Error("timer needs no memory"); }, token => {
        calls.push(token);
        completion.resolve();
      });
    const clock = modules[`${prefix}|wasi:clocks/monotonic-clock@0.2`];
    const reactor = modules[`${prefix}|netwasm:runtime/reactor-host@1`];
    reactor.watch(clock["subscribe-duration"](0n), 7);
    reactor.cancel(7);
    assert.deepEqual(calls, []);
    reactor.watch(clock["subscribe-duration"](0n), 8);
    await completion.promise;
    assert.deepEqual(calls, [8]);
    assert.ok(modules[`${prefix}|wasi:cli/environment@0.2`]);
  }
});
