import { createGuestByteReader } from "./output-sink-stream.mjs";

// wasi:http 0.3 for hosts that reach the network through fetch, such as browsers: the types
// interface's fields, requests, request options and responses, and a client whose send runs
// the request through fetch. A body is a byte stream and its trailers a future, as the
// interface has them. fetch sends no request trailers and reports no response trailers, so a
// request's trailers must be empty and a response's resolve to none.

// The names preview3-shim's Node.js host also forbids, so a request behaves alike in both.
const forbiddenNames = new Set([
  "connection",
  "http2-settings",
  "host",
  "keep-alive",
  "proxy-authenticate",
  "proxy-authorization",
  "proxy-connection",
  "transfer-encoding",
  "upgrade",
]);
const tokenPattern = /^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$/u;
const schemePattern = /^[A-Za-z][A-Za-z0-9+.-]*$/u;
const pathPattern = /^[^\s\u0000-\u001f\u007f]*$/u;
const authorityPattern = /^[^\s\u0000-\u001f\u007f/?#]+$/u;
const methodNames = ["get", "head", "post", "put", "delete", "connect", "options", "trace", "patch"];
const nanosecondsPerMillisecond = 1_000_000n;
const maximumTimerDelay = 0x7fffffff;

const ok = value => Object.freeze({ tag: "ok", val: value });
const err = value => Object.freeze({ tag: "err", val: value });
// A case without a payload, of header-error or request-options-error.
const variantCase = tag => Object.freeze({ tag });
const errorCode = (tag, val) => Object.freeze(val === undefined ? { tag } : { tag, val });

const fieldsState = new WeakMap();
const requestState = new WeakMap();
const optionsState = new WeakMap();
const responseState = new WeakMap();

function state(map, value, label) {
  const found = map.get(value);
  if (found === undefined) throw new TypeError(`${label} resource is invalid`);
  return found;
}

function bytesOf(value) {
  if (value instanceof Uint8Array) return value.slice();
  if (Array.isArray(value) && value.every(byte => Number.isInteger(byte) && byte >= 0 && byte <= 255)) {
    return Uint8Array.from(value);
  }
  throw new TypeError("field value must be bytes");
}

// A field value is any bytes but a line break or NUL.
function validValue(bytes) {
  return bytes.every(byte => byte !== 0x0a && byte !== 0x0d && byte !== 0x00);
}

function checkName(name) {
  if (typeof name !== "string" || !tokenPattern.test(name)) return variantCase("invalid-syntax");
  return forbiddenNames.has(name.toLowerCase()) ? variantCase("forbidden") : null;
}

function createFields(entries, immutable) {
  const fields = Object.create(Fields.prototype);
  fieldsState.set(fields, { entries, immutable });
  return fields;
}

const copyEntries = entries => entries.map(([name, value]) => [name, value.slice()]);

class Fields {
  constructor() {
    fieldsState.set(this, { entries: [], immutable: false });
  }

  static fromList(entries) {
    if (!Array.isArray(entries)) throw new TypeError("field entries must be a list");
    const copied = [];
    for (const entry of entries) {
      if (!Array.isArray(entry) || entry.length !== 2) throw new TypeError("field entry must be a name and value");
      const failure = checkName(entry[0]);
      if (failure !== null) return err(failure);
      const value = bytesOf(entry[1]);
      if (!validValue(value)) return err(variantCase("invalid-syntax"));
      copied.push([entry[0], value]);
    }
    return ok(createFields(copied, false));
  }

  get(name) {
    const lower = String(name).toLowerCase();
    return state(fieldsState, this, "fields").entries
      .filter(([entry]) => entry.toLowerCase() === lower)
      .map(([, value]) => value.slice());
  }

  has(name) {
    if (typeof name !== "string" || !tokenPattern.test(name)) return false;
    const lower = name.toLowerCase();
    return state(fieldsState, this, "fields").entries.some(([entry]) => entry.toLowerCase() === lower);
  }

  set(name, values) {
    const fields = state(fieldsState, this, "fields");
    if (fields.immutable) return err(variantCase("immutable"));
    const failure = checkName(name);
    if (failure !== null) return err(failure);
    if (!Array.isArray(values)) throw new TypeError("field values must be a list");
    const copied = values.map(bytesOf);
    if (!copied.every(validValue)) return err(variantCase("invalid-syntax"));
    const lower = name.toLowerCase();
    fields.entries = fields.entries.filter(([entry]) => entry.toLowerCase() !== lower);
    for (const value of copied) fields.entries.push([name, value]);
    return ok(undefined);
  }

  delete(name) {
    const result = this.getAndDelete(name);
    return result.tag === "ok" ? ok(undefined) : result;
  }

  getAndDelete(name) {
    const fields = state(fieldsState, this, "fields");
    if (fields.immutable) return err(variantCase("immutable"));
    const failure = checkName(name);
    if (failure !== null) return err(failure);
    const lower = name.toLowerCase();
    const removed = fields.entries.filter(([entry]) => entry.toLowerCase() === lower);
    fields.entries = fields.entries.filter(([entry]) => entry.toLowerCase() !== lower);
    return ok(removed.map(([, value]) => value));
  }

  append(name, value) {
    const fields = state(fieldsState, this, "fields");
    if (fields.immutable) return err(variantCase("immutable"));
    const failure = checkName(name);
    if (failure !== null) return err(failure);
    const bytes = bytesOf(value);
    if (!validValue(bytes)) return err(variantCase("invalid-syntax"));
    fields.entries.push([name, bytes]);
    return ok(undefined);
  }

  copyAll() {
    return copyEntries(state(fieldsState, this, "fields").entries);
  }

  clone() {
    return createFields(copyEntries(state(fieldsState, this, "fields").entries), false);
  }
}

// A request or response takes its headers: the guest no longer holds them, and what it reads
// back is an immutable copy.
function takeFields(value) {
  const fields = state(fieldsState, value, "fields");
  fields.immutable = true;
  return copyEntries(fields.entries);
}

class RequestOptions {
  constructor() {
    optionsState.set(this, { connect: undefined, firstByte: undefined, betweenBytes: undefined, immutable: false });
  }

  getConnectTimeout() { return state(optionsState, this, "request-options").connect; }
  setConnectTimeout(duration) { return setOption(this, "connect", duration); }
  getFirstByteTimeout() { return state(optionsState, this, "request-options").firstByte; }
  setFirstByteTimeout(duration) { return setOption(this, "firstByte", duration); }
  getBetweenBytesTimeout() { return state(optionsState, this, "request-options").betweenBytes; }
  setBetweenBytesTimeout(duration) { return setOption(this, "betweenBytes", duration); }

  clone() {
    const options = state(optionsState, this, "request-options");
    return createOptions(options, false);
  }
}

function setOption(owner, key, duration) {
  const options = state(optionsState, owner, "request-options");
  if (options.immutable) return err(variantCase("immutable"));
  if (duration !== undefined && typeof duration !== "bigint") throw new TypeError("a timeout is a duration");
  options[key] = duration === undefined ? undefined : BigInt.asUintN(64, duration);
  return ok(undefined);
}

function createOptions(source, immutable) {
  const options = Object.create(RequestOptions.prototype);
  optionsState.set(options, {
    connect: source.connect,
    firstByte: source.firstByte,
    betweenBytes: source.betweenBytes,
    immutable,
  });
  return options;
}

// The future a request's or response's transmission settles.
function transmission() {
  let settle;
  const promise = new Promise(resolve => { settle = resolve; });
  return { promise, settle };
}

class Request {
  constructor() {
    throw new TypeError("a request is created with Request.new");
  }

  static new(headers, contents, trailers, options) {
    const request = Object.create(Request.prototype);
    const sent = transmission();
    requestState.set(request, {
      headers: takeFields(headers),
      contents,
      trailers,
      options: options === undefined ? undefined : createOptions(state(optionsState, options, "request-options"), true),
      method: Object.freeze({ tag: "get" }),
      pathWithQuery: undefined,
      scheme: undefined,
      authority: undefined,
      consumed: false,
      settle: sent.settle,
    });
    return [request, sent.promise];
  }

  getMethod() { return state(requestState, this, "request").method; }

  setMethod(method) {
    const request = state(requestState, this, "request");
    if (!validMethod(method)) return err(undefined);
    request.method = Object.freeze({ ...method });
    return ok(undefined);
  }

  getPathWithQuery() { return state(requestState, this, "request").pathWithQuery; }

  setPathWithQuery(pathWithQuery) {
    return setText(this, "pathWithQuery", pathWithQuery, pathPattern);
  }

  getScheme() { return state(requestState, this, "request").scheme; }

  setScheme(scheme) {
    const request = state(requestState, this, "request");
    if (scheme !== undefined && !validScheme(scheme)) return err(undefined);
    request.scheme = scheme === undefined ? undefined : Object.freeze({ ...scheme });
    return ok(undefined);
  }

  getAuthority() { return state(requestState, this, "request").authority; }

  setAuthority(authority) {
    return setText(this, "authority", authority, authorityPattern);
  }

  getOptions() {
    const options = state(requestState, this, "request").options;
    return options === undefined ? undefined : createOptions(state(optionsState, options, "request-options"), true);
  }

  getHeaders() {
    return createFields(copyEntries(state(requestState, this, "request").headers), true);
  }

  static consumeBody(request, res) {
    const consumed = consume(requestState, request, "request");
    return [guestBody(consumed.contents), trailersOf(consumed.trailers)];
  }
}

function validMethod(method) {
  if (method === null || typeof method !== "object") return false;
  if (method.tag === "other") return typeof method.val === "string" && tokenPattern.test(method.val);
  return methodNames.includes(method.tag);
}

function validScheme(scheme) {
  if (scheme === null || typeof scheme !== "object") return false;
  if (scheme.tag === "other") return typeof scheme.val === "string" && schemePattern.test(scheme.val);
  return scheme.tag === "HTTP" || scheme.tag === "HTTPS";
}

function setText(owner, key, value, pattern) {
  const request = state(requestState, owner, "request");
  if (value !== undefined && (typeof value !== "string" || !pattern.test(value))) return err(undefined);
  request[key] = value;
  return ok(undefined);
}

function consume(map, value, label) {
  const found = state(map, value, label);
  if (found.consumed) throw new TypeError(`${label} body was already consumed`);
  found.consumed = true;
  return found;
}

// A body the guest supplies, as the bytes it writes; none is an empty body.
async function* guestBody(contents) {
  if (contents === undefined) return;
  const read = createGuestByteReader(contents);
  for (;;) {
    const chunk = await read();
    if (chunk === null) return;
    yield chunk;
  }
}

function trailersOf(trailers) {
  return Promise.resolve(trailers);
}

class Response {
  constructor() {
    throw new TypeError("a response is created with Response.new");
  }

  static new(headers, contents, trailers) {
    const sent = transmission();
    const response = createResponse(200, takeFields(headers), guestBody(contents), Promise.resolve(trailers));
    sent.settle(ok(undefined));
    return [response, sent.promise];
  }

  getStatusCode() { return state(responseState, this, "response").status; }

  setStatusCode(status) {
    const response = state(responseState, this, "response");
    if (!Number.isInteger(status) || status < 100 || status > 599) return err(undefined);
    response.status = status;
    return ok(undefined);
  }

  getHeaders() {
    return createFields(copyEntries(state(responseState, this, "response").headers), true);
  }

  static consumeBody(response, res) {
    const consumed = consume(responseState, response, "response");
    return [consumed.body, consumed.trailers];
  }
}

function createResponse(status, headers, body, trailers) {
  const response = Object.create(Response.prototype);
  responseState.set(response, { status, headers, body, trailers, consumed: false });
  return response;
}

// Text in field values crosses fetch as Latin-1, byte for byte.
const latin1 = bytes => String.fromCharCode(...bytes);
const latin1Bytes = text => Uint8Array.from(text, character => character.charCodeAt(0) & 0xff);

function milliseconds(duration) {
  return duration === undefined ? undefined
    : Number((duration + nanosecondsPerMillisecond - 1n) / nanosecondsPerMillisecond);
}

function shortest(...values) {
  const defined = values.filter(value => value !== undefined);
  return defined.length === 0 ? undefined : Math.min(...defined);
}

function startTimer(delay, onTimeout) {
  if (delay === undefined) return () => {};
  const timer = setTimeout(onTimeout, Math.min(delay, maximumTimerDelay));
  return () => clearTimeout(timer);
}

async function readBody(contents) {
  const chunks = [];
  for await (const chunk of guestBody(contents)) chunks.push(chunk);
  const body = new Uint8Array(chunks.reduce((total, chunk) => total + chunk.length, 0));
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.length;
  }
  return body;
}

// A request's trailers settle before it is sent; fetch can send none.
function trailerFailure(trailers) {
  if (trailers === null || typeof trailers !== "object" || trailers.tag === "err") {
    return trailers?.val ?? errorCode("internal-error", "the request trailers failed");
  }
  if (trailers.val !== undefined && state(fieldsState, trailers.val, "fields").entries.length !== 0) {
    return errorCode("internal-error", "fetch cannot send request trailers");
  }
  return null;
}

export function createWasiHttpFetch(services) {
  if (services === null || typeof services !== "object" || typeof services.fetch !== "function") {
    throw new TypeError("wasi:http over fetch requires a fetch function");
  }
  const { fetch } = services;

  async function send(request) {
    const outgoing = consume(requestState, request, "request");
    const fail = code => {
      outgoing.settle(err(code));
      return err(code);
    };
    const scheme = outgoing.scheme ?? Object.freeze({ tag: "HTTPS" });
    if (scheme.tag === "other" || outgoing.authority === undefined) {
      return fail(errorCode("HTTP-request-URI-invalid"));
    }
    let url;
    try {
      url = new URL(`${scheme.tag === "HTTP" ? "http" : "https"}://${outgoing.authority}${outgoing.pathWithQuery ?? "/"}`);
    } catch {
      return fail(errorCode("HTTP-request-URI-invalid"));
    }
    let body;
    let trailers;
    try {
      body = outgoing.contents === undefined ? undefined : await readBody(outgoing.contents);
      trailers = await outgoing.trailers;
    } catch (cause) {
      return fail(errorCode("internal-error", String(cause?.message ?? cause)));
    }
    const trailersFailed = trailerFailure(trailers);
    if (trailersFailed !== null) return fail(trailersFailed);

    const headers = new Headers();
    for (const [name, value] of outgoing.headers) headers.append(name, latin1(value));
    const options = outgoing.options === undefined ? {} : state(optionsState, outgoing.options, "request-options");
    const controller = new AbortController();
    let timedOut = false;
    const stopTimer = startTimer(
      shortest(milliseconds(options.connect), milliseconds(options.firstByte)),
      () => {
        timedOut = true;
        controller.abort();
      });
    let response;
    try {
      response = await fetch(url.href, {
        method: outgoing.method.tag === "other" ? outgoing.method.val : outgoing.method.tag.toUpperCase(),
        headers,
        body,
        signal: controller.signal,
      });
    } catch (cause) {
      return fail(timedOut ? errorCode("connection-timeout")
        : errorCode("internal-error", String(cause?.message ?? cause)));
    } finally {
      stopTimer();
    }
    outgoing.settle(ok(undefined));
    const entries = [];
    response.headers.forEach((value, name) => entries.push([name, latin1Bytes(value)]));
    return ok(receive(response, entries, milliseconds(options.betweenBytes), controller));
  }

  return Object.freeze({
    "wasi:http/types@0.3.2": Object.freeze({ Fields, Request, RequestOptions, Response }),
    "wasi:http/client@0.3.2": Object.freeze({ send }),
  });
}

// A fetched response's body streams as the guest reads it; its trailers resolve to none once
// the body ends, or to the error that ended it.
function receive(response, headers, betweenBytes, controller) {
  let settleTrailers;
  const trailers = new Promise(resolve => { settleTrailers = resolve; });
  async function* body() {
    const reader = response.body?.getReader();
    if (reader === undefined) {
      settleTrailers(ok(undefined));
      return;
    }
    let completed = false;
    try {
      for (;;) {
        let timedOut = false;
        const stopTimer = startTimer(betweenBytes, () => {
          timedOut = true;
          controller.abort();
        });
        let next;
        try {
          next = await reader.read();
        } catch {
          settleTrailers(err(errorCode(timedOut ? "connection-read-timeout" : "HTTP-response-incomplete")));
          return;
        } finally {
          stopTimer();
        }
        if (next.done) {
          completed = true;
          settleTrailers(ok(undefined));
          return;
        }
        yield next.value;
      }
    } finally {
      // A reader that stops early cancels the rest of the body.
      if (!completed) {
        settleTrailers(err(errorCode("HTTP-response-incomplete")));
        await reader.cancel().catch(() => {});
      }
    }
  }
  return createResponse(response.status, headers, body(), trailers);
}
