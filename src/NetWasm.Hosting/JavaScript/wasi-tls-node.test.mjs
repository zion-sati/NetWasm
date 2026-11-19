// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import assert from "node:assert/strict";
import { once } from "node:events";
import { connect as connectTcp } from "node:net";
import test from "node:test";
import { connect as connectTls, createServer } from "node:tls";

import { createNodeWasiTls } from "./wasi-tls-node.mjs";

// Test-only credentials: a CA and a certificate it signed for localhost and 127.0.0.1, valid until 2126.
const testCa = `-----BEGIN CERTIFICATE-----
MIIBmzCCAUGgAwIBAgIUN5xz8GJptBcuj+RAmXQfiohNj9kwCgYIKoZIzj0EAwIw
GjEYMBYGA1UEAwwPTmV0V2FzbSBUZXN0IENBMCAXDTI2MTExNTEzMTAzOVoYDzIx
MjYxMDIyMTMxMDM5WjAaMRgwFgYDVQQDDA9OZXRXYXNtIFRlc3QgQ0EwWTATBgcq
hkjOPQIBBggqhkjOPQMBBwNCAAQR16QJM7fkLSDUgVjtqDF4wssPOJ/KE5mtn+4o
0sYArXzwsl0MhSNDKNWWc1NOTqSsfjm7DsQb97ak607lqw84o2MwYTAdBgNVHQ4E
FgQUPztF5pxwNX3wXjfK5jfnp+yCfWYwHwYDVR0jBBgwFoAUPztF5pxwNX3wXjfK
5jfnp+yCfWYwDwYDVR0TAQH/BAUwAwEB/zAOBgNVHQ8BAf8EBAMCAQYwCgYIKoZI
zj0EAwIDSAAwRQIgb+lIzFboO5otej+aa4byI6QSkHLBLGN7LH2wGI07jyICIQDP
HPh1g5rcRN1m+5CFR7yZ/NyGtK5S/KQZLfg9lqS8fw==
-----END CERTIFICATE-----`;
const serverCertificate = `-----BEGIN CERTIFICATE-----
MIIBsDCCAVagAwIBAgIUCvrHxKpJqHD/LxqvcrfyYgtm3qgwCgYIKoZIzj0EAwIw
GjEYMBYGA1UEAwwPTmV0V2FzbSBUZXN0IENBMCAXDTI2MTExNTEzMTAzOVoYDzIx
MjYxMDIyMTMxMDM5WjAUMRIwEAYDVQQDDAlsb2NhbGhvc3QwWTATBgcqhkjOPQIB
BggqhkjOPQMBBwNCAATkuwWrNHhkmsCqSMfYxKMeFyjgftc1ZZfGqf1pVA3dvDyU
fj6svbLkU52L2zTo1vDk07/QLKn2BzLArbWcoo4Ro34wfDAaBgNVHREEEzARggls
b2NhbGhvc3SHBH8AAAEwCQYDVR0TBAIwADATBgNVHSUEDDAKBggrBgEFBQcDATAd
BgNVHQ4EFgQU1hPBg4TwLpW7y5Nw3F/36Ob2Y2MwHwYDVR0jBBgwFoAUPztF5pxw
NX3wXjfK5jfnp+yCfWYwCgYIKoZIzj0EAwIDSAAwRQIhAJtiZUcgS5Z5BMJm2IhS
uz7xtY/OI6BXYmQ34AhuuCP3AiBeOli10F9t5+2DN5VxDh6xGEJxOXX/3ysgZ3g3
gUu2ww==
-----END CERTIFICATE-----`;
const serverKey = `-----BEGIN PRIVATE KEY-----
MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQginiMmv5M+efSJBBB
mAvhlveVPaJrmrHDZJUlGHas5wyhRANCAATkuwWrNHhkmsCqSMfYxKMeFyjgftc1
ZZfGqf1pVA3dvDyUfj6svbLkU52L2zTo1vDk07/QLKn2BzLArbWcoo4R
-----END PRIVATE KEY-----`;

const encoder = new TextEncoder();
const decoder = new TextDecoder();
const ok = { tag: "ok", val: undefined };

// A server that answers each chunk in upper case and closes after its client does.
async function upperCaseServer(t, onConnection = socket => {
  socket.on("data", data => socket.write(data.toString().toUpperCase()));
  socket.on("end", () => socket.end());
}) {
  const server = createServer({ key: serverKey, cert: serverCertificate }, onConnection);
  server.on("tlsClientError", () => {});
  server.listen(0, "127.0.0.1");
  await once(server, "listening");
  t.after(() => server.close());
  return server.address().port;
}

// What a guest's stream would be: chunks the test pushes, then an end.
function outgoing() {
  const chunks = [];
  let wake = null;
  let ended = false;
  return {
    write(text) { chunks.push(typeof text === "string" ? encoder.encode(text) : text); wake?.(); },
    end() { ended = true; wake?.(); },
    async* [Symbol.asyncIterator]() {
      for (;;) {
        if (chunks.length !== 0) { yield chunks.shift(); continue; }
        if (ended) return;
        await new Promise(resolve => { wake = resolve; });
        wake = null;
      }
    },
  };
}

// A guest's side of one connection: the TCP socket is the transport the guest moves ciphertext over.
async function guest(t, port, options = {}) {
  const { trust = true, extraCiphertext = null } = options;
  const network = { corrupt: false };
  const tls = createNodeWasiTls({ connect: settings => connectTls(trust ? { ...settings, ca: testCa } : settings) });
  const { Connector } = tls["wasi:tls/client@0.3.0-draft"];
  const tcp = connectTcp(port, "127.0.0.1");
  await once(tcp, "connect");
  t.after(() => tcp.destroy());
  const cleartext = outgoing();
  const connector = new Connector();
  const [ciphertext, sent] = connector.send(cleartext);
  // As a guest's stream reaches the host: an async iterator of chunks.
  const fromNetwork = (async function* () {
    for await (const chunk of tcp) {
      // A record whose last byte (its authentication tag's) is flipped fails to decrypt.
      if (network.corrupt) chunk[chunk.length - 1] ^= 0xff;
      yield chunk;
    }
    if (extraCiphertext !== null) yield extraCiphertext;
  })();
  const [decrypted, received] = connector.receive(fromNetwork);
  const copied = (async () => {
    for await (const chunk of ciphertext) {
      if (!tcp.destroyed) tcp.write(chunk);
    }
    tcp.end();
  })();
  return { tls, Connector, connector, cleartext, sent, decrypted, received, copied, network, tcp };
}

async function readAll(stream) {
  let text = "";
  for await (const chunk of stream) text += decoder.decode(chunk);
  return text;
}

test("the provider requires node:tls's connect", () => {
  for (const services of [null, {}, { connect: 1 }]) {
    assert.throws(() => createNodeWasiTls(services), /requires node:tls's connect/);
  }
});

test("a connector authenticates the server, carries data both ways and closes with close_notify", async t => {
  const port = await upperCaseServer(t);
  const side = await guest(t, port);
  assert.deepEqual(await side.Connector.connect(side.connector, "localhost"), ok);
  side.cleartext.write("hello, ");
  side.cleartext.write("tls");
  side.cleartext.end();
  assert.equal(await readAll(side.decrypted), "HELLO, TLS");
  assert.deepEqual(await side.sent, ok);
  assert.deepEqual(await side.received, ok);
  await side.copied;
});

test("an IP address names the server by its address", async t => {
  const port = await upperCaseServer(t);
  const side = await guest(t, port);
  assert.deepEqual(await side.Connector.connect(side.connector, "127.0.0.1"), ok);
  side.cleartext.end();
  assert.equal(await readAll(side.decrypted), "");
});

test("a megabyte crosses with the guest's backpressure", async t => {
  const port = await upperCaseServer(t);
  const side = await guest(t, port);
  assert.deepEqual(await side.Connector.connect(side.connector, "localhost"), ok);
  const block = "abcdefghijklmnop".repeat(4096);
  for (let index = 0; index < 16; index++) side.cleartext.write(block);
  side.cleartext.end();
  const echoed = await readAll(side.decrypted);
  assert.equal(echoed.length, block.length * 16);
  assert.equal(echoed.slice(0, 16), "ABCDEFGHIJKLMNOP");
  assert.deepEqual(await side.sent, ok);
});

test("a server that sends faster than the guest reads waits for the guest", async t => {
  const block = "0123456789abcdef".repeat(8192);
  const port = await upperCaseServer(t, socket => {
    for (let index = 0; index < 32; index++) socket.write(block);
    socket.end();
  });
  const side = await guest(t, port);
  assert.deepEqual(await side.Connector.connect(side.connector, "localhost"), ok);
  // Nothing reads the cleartext for a while: node:tls stops pulling, and the transport stops taking ciphertext.
  await new Promise(resolve => setTimeout(resolve, 100));
  side.cleartext.end();
  const received = await readAll(side.decrypted);
  assert.equal(received.length, block.length * 32);
  assert.deepEqual(await side.received, ok);
});

test("a name the certificate does not cover fails the handshake and both transforms", async t => {
  const port = await upperCaseServer(t);
  const side = await guest(t, port);
  const result = await side.Connector.connect(side.connector, "wrong.invalid");
  assert.equal(result.tag, "err");
  assert.match(result.val.toDebugString(), /wrong\.invalid|altnames|Hostname/u);
  assert.equal(await readAll(side.decrypted), "");
  assert.equal((await side.sent).tag, "err");
  assert.equal((await side.received).tag, "err");
});

test("a certificate from an untrusted CA fails, and ciphertext arriving afterwards is ignored", async t => {
  const port = await upperCaseServer(t);
  const side = await guest(t, port, { trust: false, extraCiphertext: Uint8Array.of(23, 3, 3, 0, 1, 0) });
  const result = await side.Connector.connect(side.connector, "localhost");
  assert.equal(result.tag, "err");
  assert.match(result.val.toDebugString(), /certificate/u);
  assert.equal((await side.received).tag, "err");
  // The client's alert ends the connection; the guest's last ciphertext then reaches a destroyed transport.
  await side.copied;
  if (!side.tcp.closed) await once(side.tcp, "close");
  await new Promise(resolve => setImmediate(resolve));
});

test("a record that fails to decrypt fails the cleartext and both transforms", async t => {
  const port = await upperCaseServer(t);
  const side = await guest(t, port);
  assert.deepEqual(await side.Connector.connect(side.connector, "localhost"), ok);
  side.network.corrupt = true;
  side.cleartext.write("first");
  await assert.rejects(readAll(side.decrypted), /decrypt|bad record mac|BAD_DECRYPT/iu);
  side.cleartext.write("second");
  assert.equal((await side.sent).tag, "err");
  assert.equal((await side.received).tag, "err");
});

test("each transform is configured once, before the handshake", async t => {
  const port = await upperCaseServer(t);
  const side = await guest(t, port);
  const [again, sentAgain] = side.connector.send(outgoing());
  assert.equal(await readAll(again), "");
  assert.equal((await sentAgain).val.toDebugString(), "send already configured");
  const spare = outgoing();
  spare.write("ignored");
  spare.end();
  const [receivedAgain, resultAgain] = side.connector.receive(spare);
  assert.equal(await readAll(receivedAgain), "");
  assert.equal((await resultAgain).val.toDebugString(), "receive already configured");

  const { Connector } = side;
  const unsent = new Connector();
  assert.equal((await Connector.connect(unsent, "localhost")).val.toDebugString(),
    "send() must be called before connect()");
  const unreceived = new Connector();
  unreceived.send(outgoing());
  assert.equal((await Connector.connect(unreceived, "localhost")).val.toDebugString(),
    "receive() must be called before connect()");
  const unnamed = new Connector();
  unnamed.send(outgoing());
  unnamed.receive(outgoing());
  await assert.rejects(Connector.connect(unnamed, 7), /server name must be a string/u);
  await assert.rejects(Connector.connect(unnamed, "localhost"), /connector resource is invalid/u);
});

test("errors and connectors are the host's resources", async t => {
  const port = await upperCaseServer(t);
  const side = await guest(t, port);
  const { Error: TlsError } = side.tls["wasi:tls/types@0.3.0-draft"];
  assert.throws(() => new TlsError(), /created by the host/u);
  assert.throws(() => TlsError.prototype.toDebugString.call({}), /error resource is invalid/u);
  assert.throws(() => side.Connector.prototype.send.call({}, outgoing()), /connector resource is invalid/u);
  assert.throws(() => side.Connector.prototype.receive.call({}, outgoing()), /connector resource is invalid/u);
});
