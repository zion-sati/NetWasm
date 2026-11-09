import assert from "node:assert/strict";
import test from "node:test";

import { createWasiHttpFetch } from "./wasi-http-fetch.mjs";

const encoder = new TextEncoder();
const decoder = new TextDecoder();
const bytes = text => encoder.encode(text);
const ok = val => ({ tag: "ok", val });

function http(fetch = async () => new Response("")) {
  const modules = createWasiHttpFetch(Object.freeze({ fetch }));
  return {
    modules,
    types: modules["wasi:http/types@0.3.2"],
    client: modules["wasi:http/client@0.3.2"],
  };
}

// A guest stream, as a raw host hands it over: an async iterator of chunks.
function guestStream(chunks) {
  return { async *[Symbol.asyncIterator]() { yield* chunks; } };
}

async function drain(stream) {
  const chunks = [];
  for await (const chunk of stream) chunks.push(...chunk);
  return decoder.decode(Uint8Array.from(chunks));
}

function request(types, { headers = [], contents, trailers = ok(undefined), options } = {}) {
  const fields = types.Fields.fromList(headers).val;
  const [outgoing, sent] = types.Request.new(fields, contents, Promise.resolve(trailers), options);
  outgoing.setScheme({ tag: "HTTP" });
  outgoing.setAuthority("example.test:8080");
  outgoing.setPathWithQuery("/items?q=1");
  return { fields, outgoing, sent };
}

test("fields keep names' casing, match names without it and enforce their rules", () => {
  const { types } = http();
  assert.equal(Object.isFrozen(types), true);
  const fields = types.Fields.fromList([["Accept", bytes("a")], ["accept", [98]]]).val;
  assert.deepEqual(fields.get("ACCEPT").map(value => decoder.decode(value)), ["a", "b"]);
  assert.equal(fields.has("Accept"), true);
  assert.equal(fields.has("missing"), false);
  assert.equal(fields.has("bad name"), false);
  assert.equal(fields.has(1), false);
  assert.deepEqual(fields.append("X-Id", bytes("1")), ok(undefined));
  assert.deepEqual(fields.set("accept", [bytes("c"), bytes("d")]), ok(undefined));
  assert.deepEqual(fields.copyAll().map(([name, value]) => [name, decoder.decode(value)]),
    [["X-Id", "1"], ["accept", "c"], ["accept", "d"]]);
  assert.deepEqual(fields.getAndDelete("Accept").val.map(value => decoder.decode(value)), ["c", "d"]);
  assert.deepEqual(fields.delete("x-id"), ok(undefined));
  assert.deepEqual(fields.copyAll(), []);

  const empty = new types.Fields();
  assert.deepEqual(empty.copyAll(), []);
  const copy = types.Fields.fromList([["a", bytes("1")]]).val.clone();
  assert.deepEqual(copy.append("b", bytes("2")), ok(undefined));
  assert.equal(copy.copyAll().length, 2);

  for (const [entries, tag] of [
    [[["bad name", bytes("x")]], "invalid-syntax"],
    [[["Host", bytes("x")]], "forbidden"],
    [[["ok", bytes("line\nbreak")]], "invalid-syntax"],
  ]) {
    assert.deepEqual(types.Fields.fromList(entries), { tag: "err", val: { tag } });
  }
  const mutable = new types.Fields();
  assert.deepEqual(mutable.set("connection", []), { tag: "err", val: { tag: "forbidden" } });
  assert.deepEqual(mutable.set("ok", [bytes("\r")]), { tag: "err", val: { tag: "invalid-syntax" } });
  assert.deepEqual(mutable.append("ok", [0]), { tag: "err", val: { tag: "invalid-syntax" } });
  assert.deepEqual(mutable.append("a b", bytes("x")), { tag: "err", val: { tag: "invalid-syntax" } });
  assert.deepEqual(mutable.delete("upgrade"), { tag: "err", val: { tag: "forbidden" } });
  assert.throws(() => mutable.set("ok", "x"), /values must be a list/);
  assert.throws(() => mutable.append("ok", "x"), /must be bytes/);
  assert.throws(() => mutable.append("ok", [256]), /must be bytes/);
  assert.throws(() => types.Fields.fromList("x"), /must be a list/);
  assert.throws(() => types.Fields.fromList([["only"]]), /name and value/);
  assert.throws(() => types.Fields.prototype.copyAll.call({}), /fields resource is invalid/);
});

test("a request takes its headers and reads back immutable copies", () => {
  const { types } = http();
  const { fields, outgoing } = request(types, { headers: [["Accept", bytes("*/*")]] });
  for (const immutable of [fields, outgoing.getHeaders()]) {
    assert.deepEqual(immutable.set("a", []), { tag: "err", val: { tag: "immutable" } });
    assert.deepEqual(immutable.append("a", bytes("1")), { tag: "err", val: { tag: "immutable" } });
    assert.deepEqual(immutable.delete("a"), { tag: "err", val: { tag: "immutable" } });
  }
  assert.deepEqual(outgoing.getHeaders().clone().append("a", bytes("1")), ok(undefined));
  assert.equal(outgoing.getHeaders().copyAll().length, 1);
  assert.throws(() => new types.Request(), /Request.new/);
});

test("a request's method, target and options follow the interface's syntax", () => {
  const { types } = http();
  const [outgoing] = types.Request.new(new types.Fields(), undefined, Promise.resolve(ok(undefined)), undefined);
  assert.deepEqual(outgoing.getMethod(), { tag: "get" });
  assert.equal(outgoing.getScheme(), undefined);
  assert.equal(outgoing.getAuthority(), undefined);
  assert.equal(outgoing.getPathWithQuery(), undefined);
  assert.equal(outgoing.getOptions(), undefined);
  assert.deepEqual(outgoing.setMethod({ tag: "post" }), ok(undefined));
  assert.deepEqual(outgoing.setMethod({ tag: "other", val: "PURGE" }), ok(undefined));
  assert.deepEqual(outgoing.getMethod(), { tag: "other", val: "PURGE" });
  for (const method of [null, { tag: "fetch" }, { tag: "other", val: "BAD METHOD" }, { tag: "other", val: 1 }]) {
    assert.deepEqual(outgoing.setMethod(method), { tag: "err", val: undefined });
  }
  assert.deepEqual(outgoing.setScheme({ tag: "other", val: "ws+tls" }), ok(undefined));
  assert.deepEqual(outgoing.setScheme(undefined), ok(undefined));
  for (const scheme of [null, { tag: "FTP" }, { tag: "other", val: "1bad" }, { tag: "other", val: 2 }]) {
    assert.deepEqual(outgoing.setScheme(scheme), { tag: "err", val: undefined });
  }
  assert.deepEqual(outgoing.setAuthority("user@host:1"), ok(undefined));
  assert.deepEqual(outgoing.setAuthority("bad/host"), { tag: "err", val: undefined });
  assert.deepEqual(outgoing.setPathWithQuery("/a b"), { tag: "err", val: undefined });
  assert.deepEqual(outgoing.setPathWithQuery(3), { tag: "err", val: undefined });
  assert.deepEqual(outgoing.setPathWithQuery(undefined), ok(undefined));

  const options = new types.RequestOptions();
  assert.equal(options.getConnectTimeout(), undefined);
  assert.deepEqual(options.setConnectTimeout(5n), ok(undefined));
  assert.deepEqual(options.setFirstByteTimeout(-1n), ok(undefined));
  assert.deepEqual(options.setBetweenBytesTimeout(undefined), ok(undefined));
  assert.equal(options.getFirstByteTimeout(), 0xffffffffffffffffn);
  assert.equal(options.getBetweenBytesTimeout(), undefined);
  assert.throws(() => options.setConnectTimeout(5), /duration/);
  const [withOptions] = types.Request.new(new types.Fields(), undefined, Promise.resolve(ok(undefined)), options);
  const held = withOptions.getOptions();
  assert.equal(held.getConnectTimeout(), 5n);
  assert.deepEqual(held.setConnectTimeout(1n), { tag: "err", val: { tag: "immutable" } });
  assert.deepEqual(held.clone().setConnectTimeout(1n), ok(undefined));
});

test("a request's body is the guest's stream and its trailers the guest's future", async () => {
  const { types } = http();
  const [withBody] = types.Request.new(new types.Fields(), guestStream([bytes("ab"), [99]]),
    Promise.resolve(ok(undefined)), undefined);
  const [body, trailers] = types.Request.consumeBody(withBody, Promise.resolve(ok(undefined)));
  assert.equal(await drain(body), "abc");
  assert.deepEqual(await trailers, ok(undefined));
  assert.throws(() => types.Request.consumeBody(withBody), /already consumed/);
  const [empty] = types.Request.new(new types.Fields(), undefined, Promise.resolve(ok(undefined)), undefined);
  assert.equal(await drain(types.Request.consumeBody(empty)[0]), "");
});

test("send runs the request through fetch and streams the response", async () => {
  let seen;
  const { types, client } = http(async (url, init) => {
    seen = { url, method: init.method, headers: [...init.headers], body: init.body, signal: init.signal };
    return new Response("hello world", { status: 201, headers: { "X-Reply": "yes" } });
  });
  const options = new types.RequestOptions();
  options.setFirstByteTimeout(5_000_000_000n);
  const { outgoing, sent } = request(types, {
    headers: [["Accept", bytes("text/plain")]],
    contents: guestStream([bytes("pay"), bytes("load")]),
    options,
  });
  outgoing.setMethod({ tag: "post" });
  const result = await client.send(outgoing);
  assert.equal(result.tag, "ok");
  assert.deepEqual(await sent, ok(undefined));
  assert.equal(seen.url, "http://example.test:8080/items?q=1");
  assert.equal(seen.method, "POST");
  assert.deepEqual(seen.headers, [["accept", "text/plain"]]);
  assert.equal(decoder.decode(seen.body), "payload");
  assert.equal(seen.signal.aborted, false);
  const response = result.val;
  assert.equal(response.getStatusCode(), 201);
  assert.deepEqual(response.getHeaders().get("x-reply").map(value => decoder.decode(value)), ["yes"]);
  assert.deepEqual(response.getHeaders().set("a", []), { tag: "err", val: { tag: "immutable" } });
  const [body, trailers] = types.Response.consumeBody(response, Promise.resolve(ok(undefined)));
  assert.equal(await drain(body), "hello world");
  assert.deepEqual(await trailers, ok(undefined));
  assert.throws(() => types.Response.consumeBody(response), /already consumed/);
  await assert.rejects(client.send(outgoing), /already consumed/);
});

test("send reaches defaults: HTTPS, the root path and GET without a body", async () => {
  let seen;
  const { types, client } = http(async (url, init) => {
    seen = { url, method: init.method, body: init.body };
    return new Response(null, { status: 204 });
  });
  const [outgoing] = types.Request.new(new types.Fields(), undefined, Promise.resolve(ok(undefined)), undefined);
  outgoing.setAuthority("example.test");
  outgoing.setMethod({ tag: "other", val: "PURGE" });
  const response = (await client.send(outgoing)).val;
  assert.deepEqual(seen, { url: "https://example.test/", method: "PURGE", body: undefined });
  const [body, trailers] = types.Response.consumeBody(response);
  assert.equal(await drain(body), "");
  assert.deepEqual(await trailers, ok(undefined));
});

test("send reports what it cannot send or receive as an error code", async () => {
  const failing = cause => http(async () => { throw cause; });
  const cases = [
    [request => request.setScheme({ tag: "other", val: "ftp" }), "HTTP-request-URI-invalid"],
    [request => request.setAuthority(undefined), "HTTP-request-URI-invalid"],
    [request => request.setAuthority("[::1"), "HTTP-request-URI-invalid"],
  ];
  for (const [mutate, tag] of cases) {
    const { types, client } = http();
    const { outgoing, sent } = request(types);
    mutate(outgoing);
    const result = await client.send(outgoing);
    assert.equal(result.val.tag, tag);
    assert.deepEqual(await sent, result);
  }

  const network = failing(new TypeError("Failed to fetch"));
  const refused = await network.client.send(request(network.types).outgoing);
  assert.deepEqual(refused, { tag: "err", val: { tag: "internal-error", val: "Failed to fetch" } });
  const plain = failing("no message");
  assert.equal((await plain.client.send(request(plain.types).outgoing)).val.val, "no message");

  const trailerCases = [
    [Promise.resolve({ tag: "err", val: { tag: "HTTP-request-denied" } }), "HTTP-request-denied"],
    [Promise.resolve(null), "internal-error"],
    [Promise.reject(new Error("guest trapped")), "internal-error"],
    [Promise.reject("guest dropped"), "internal-error"],
  ];
  for (const [trailers, tag] of trailerCases) {
    const { types, client } = http();
    const [outgoing] = types.Request.new(new types.Fields(), undefined, trailers, undefined);
    outgoing.setAuthority("example.test");
    assert.equal((await client.send(outgoing)).val.tag, tag);
  }
  const { types, client } = http();
  const [withTrailers] = types.Request.new(new types.Fields(), undefined,
    Promise.resolve(ok(types.Fields.fromList([["Digest", bytes("x")]]).val)), undefined);
  withTrailers.setAuthority("example.test");
  assert.match((await client.send(withTrailers)).val.val, /cannot send request trailers/);
  const [emptyTrailers] = types.Request.new(new types.Fields(), undefined,
    Promise.resolve(ok(new types.Fields())), undefined);
  emptyTrailers.setAuthority("example.test");
  assert.equal((await client.send(emptyTrailers)).tag, "ok");
});

test("request timeouts abort the fetch and the body reads", async () => {
  const hanging = http((url, init) => new Promise((_, reject) => {
    init.signal.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError")));
  }));
  const options = new hanging.types.RequestOptions();
  options.setConnectTimeout(1_000_000n);
  options.setFirstByteTimeout(50_000_000n);
  const timedOut = await hanging.client.send(request(hanging.types, { options }).outgoing);
  assert.deepEqual(timedOut, { tag: "err", val: { tag: "connection-timeout" } });

  let controller;
  const slow = http(async (url, init) => new Response(new ReadableStream({
    start(stream) {
      controller = stream;
      stream.enqueue(bytes("first"));
      init.signal.addEventListener("abort", () => stream.error(new DOMException("aborted", "AbortError")));
    },
  })));
  const slowOptions = new slow.types.RequestOptions();
  slowOptions.setBetweenBytesTimeout(20_000_000n);
  const response = (await slow.client.send(request(slow.types, { options: slowOptions }).outgoing)).val;
  const [body, trailers] = slow.types.Response.consumeBody(response);
  const read = [];
  for await (const chunk of body) read.push(decoder.decode(chunk));
  assert.deepEqual(read, ["first"]);
  assert.deepEqual(await trailers, { tag: "err", val: { tag: "connection-read-timeout" } });
  assert.ok(controller);
});

test("a body that fails or is left early ends its trailers with an error", async () => {
  const broken = http(async () => new Response(new ReadableStream({
    pull(stream) { stream.error(new Error("reset")); },
  })));
  const response = (await broken.client.send(request(broken.types).outgoing)).val;
  const [body, trailers] = broken.types.Response.consumeBody(response);
  assert.equal(await drain(body), "");
  assert.deepEqual(await trailers, { tag: "err", val: { tag: "HTTP-response-incomplete" } });

  let cancelled = false;
  const endless = http(async () => new Response(new ReadableStream({
    pull(stream) { stream.enqueue(bytes("more")); },
    cancel() { cancelled = true; },
  })));
  const long = (await endless.client.send(request(endless.types).outgoing)).val;
  const [longBody, longTrailers] = endless.types.Response.consumeBody(long);
  const iterator = longBody[Symbol.asyncIterator]();
  assert.equal(decoder.decode((await iterator.next()).value), "more");
  await iterator.return();
  assert.equal(cancelled, true);
  assert.deepEqual(await longTrailers, { tag: "err", val: { tag: "HTTP-response-incomplete" } });
});

test("a response the guest builds carries its status, headers, body and trailers", async () => {
  const { types } = http();
  const [response, sent] = types.Response.new(
    types.Fields.fromList([["Content-Type", bytes("text/plain")]]).val,
    guestStream([bytes("ok")]),
    Promise.resolve(ok(undefined)));
  assert.deepEqual(await sent, ok(undefined));
  assert.equal(response.getStatusCode(), 200);
  assert.deepEqual(response.setStatusCode(404), ok(undefined));
  assert.equal(response.getStatusCode(), 404);
  for (const status of [99, 600, 1.5]) {
    assert.deepEqual(response.setStatusCode(status), { tag: "err", val: undefined });
  }
  assert.equal(response.getHeaders().copyAll().length, 1);
  const [body, trailers] = types.Response.consumeBody(response);
  assert.equal(await drain(body), "ok");
  assert.deepEqual(await trailers, ok(undefined));
  assert.throws(() => new types.Response(), /Response.new/);
  assert.throws(() => types.Response.prototype.getStatusCode.call({}), /response resource is invalid/);
});

test("rejects services without fetch", () => {
  for (const services of [null, {}, { fetch: 1 }]) {
    assert.throws(() => createWasiHttpFetch(services), /requires a fetch function/);
  }
});
