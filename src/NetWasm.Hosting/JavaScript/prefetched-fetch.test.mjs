// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import test from "node:test";

import { createPrefetchedFetch } from "./prefetched-fetch.mjs";

const wasmUrl = "https://example.test/app/App.wasm";
const manifestUrl = "https://example.test/app/deployment.json";

function recordingFetch() {
  const calls = [];
  const fetch = (input, init) => {
    calls.push([input, init]);
    return Promise.resolve(`fetched ${input}`);
  };
  return { calls, fetch };
}

test("hands each prefetched response out once and fetches everything else", async () => {
  const { calls, fetch } = recordingFetch();
  const wasm = Promise.resolve("prefetched wasm");
  const responses = new Map([[wasmUrl, wasm]]);
  const prefetchedFetch = createPrefetchedFetch({ fetch, responses });

  assert.equal(prefetchedFetch(wasmUrl, { signal: undefined }), wasm);
  assert.equal(responses.size, 0);
  assert.equal(await prefetchedFetch(wasmUrl), `fetched ${wasmUrl}`);
  assert.equal(await prefetchedFetch(manifestUrl, { signal: null }), `fetched ${manifestUrl}`);
  assert.deepEqual(calls, [[wasmUrl, undefined], [manifestUrl, { signal: null }]]);
});

test("a request with an abort signal fetches itself and leaves the prefetched response", async () => {
  const { calls, fetch } = recordingFetch();
  const wasm = Promise.resolve("prefetched wasm");
  const responses = new Map([[wasmUrl, wasm]]);
  const signal = new AbortController().signal;

  assert.equal(await createPrefetchedFetch({ fetch, responses })(wasmUrl, { signal }), `fetched ${wasmUrl}`);
  assert.deepEqual(calls, [[wasmUrl, { signal }]]);
  assert.equal(responses.get(wasmUrl), wasm);
});

test("a URL object or Request input fetches itself", async () => {
  const { calls, fetch } = recordingFetch();
  const responses = new Map([[wasmUrl, Promise.resolve("prefetched wasm")]]);
  const url = new URL(wasmUrl);

  await createPrefetchedFetch({ fetch, responses })(url);
  assert.deepEqual(calls, [[url, undefined]]);
  assert.equal(responses.size, 1);
});

test("a rejected prefetch reaches the request that takes it", async () => {
  const { fetch } = recordingFetch();
  const failure = new TypeError("network");
  const rejected = Promise.reject(failure);
  rejected.catch(() => {});
  const prefetchedFetch = createPrefetchedFetch({ fetch, responses: new Map([[wasmUrl, rejected]]) });

  await assert.rejects(prefetchedFetch(wasmUrl), failure);
});

test("rejects a missing fetch, a responses value that is not a Map and invalid entries", () => {
  const { fetch } = recordingFetch();
  for (const options of [null, "fetch", {}, { fetch: "fetch", responses: new Map() }]) {
    assert.throws(() => createPrefetchedFetch(options), /requires a fetch action/u);
  }
  for (const responses of [undefined, {}, [[wasmUrl, Promise.resolve()]]]) {
    assert.throws(() => createPrefetchedFetch({ fetch, responses }), /must be a Map/u);
  }
  for (const url of [1, "App.wasm", "https://example.test/app/./App.wasm", "https://EXAMPLE.test/app/App.wasm"]) {
    assert.throws(() => createPrefetchedFetch({ fetch, responses: new Map([[url, Promise.resolve()]]) }),
      /canonical absolute URLs/u);
  }
  for (const response of [null, "response", { then: "no" }]) {
    assert.throws(() => createPrefetchedFetch({ fetch, responses: new Map([[wasmUrl, response]]) }),
      /must be a promise/u);
  }
});
