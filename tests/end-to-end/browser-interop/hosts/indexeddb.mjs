// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

// IndexedDB for both hosts. The databases and transactions stay in this module and C# holds them by number. A
// request's promise settles in the request's success or error event, while its transaction is still active.
const objects = new Map();
let nextId = 1;

function keep(value) {
  const id = nextId++;
  objects.set(id, value);
  return id;
}

function take(id) {
  if (!objects.has(id)) throw new Error(`no IndexedDB object ${id}`);
  return objects.get(id);
}

function factory() {
  if (typeof indexedDB === "undefined") throw new Error("IndexedDB is unavailable in this host");
  return indexedDB;
}

function settled(request) {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

export async function open(name) {
  await settled(factory().deleteDatabase(name));
  const request = factory().open(name, 1);
  request.onupgradeneeded = () => request.result.createObjectStore("values");
  return keep({ database: await settled(request), name });
}

export async function drop(id) {
  const { database, name } = take(id);
  objects.delete(id);
  database.close();
  await settled(factory().deleteDatabase(name));
}

// A read-write transaction, and a promise of how it finished: "complete", or "aborted: " and the error's name.
export function begin(id) {
  const transaction = take(id).database.transaction("values", "readwrite");
  const finished = new Promise(resolve => {
    transaction.oncomplete = () => resolve("complete");
    transaction.onabort = () => resolve(`aborted: ${transaction.error?.name}`);
  });
  return keep({ transaction, finished });
}

export function finished(id) {
  return take(id).finished;
}

// A request resolves with its result. A request the transaction no longer takes throws at once, and a failed
// request has an error: either resolves with the error's name, which C# checks.
function request(id, issue, result) {
  let request;
  try {
    request = issue(take(id).transaction.objectStore("values"));
  } catch (error) {
    return Promise.resolve(String(error?.name ?? error));
  }
  return new Promise(resolve => {
    request.onsuccess = () => resolve(result(request.result));
    request.onerror = event => {
      event.preventDefault();
      resolve(String(request.error?.name));
    };
  });
}

export function put(id, key, value) {
  return request(id, store => store.put(value, key), () => "ok");
}

export function get(id, key) {
  return request(id, store => store.get(key), value => String(value));
}

export function nextTask() {
  return new Promise(resolve => setTimeout(resolve, 0));
}

// A promise C# awaits, a reaction page code adds to it afterwards, and the order the two ran in.
let held;
let release;
const order = [];

export function hold() {
  held = new Promise(resolve => { release = resolve; });
  return held;
}

export function attachLater() {
  held.then(() => order.push("JS"));
}

export function releaseHeld() {
  release();
}

export function record(entry) {
  order.push(entry);
}

export function recorded() {
  const text = order.join(",");
  order.length = 0;
  return text;
}
