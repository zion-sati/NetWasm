// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { isIP } from "node:net";
import { Duplex } from "node:stream";

import { createGuestByteReader } from "./output-sink-stream.mjs";

// wasi:tls@0.3.0-draft's client for Node.js hosts, as Wasmtime 50 implements it behind -S tls: a connector is two
// stream transforms the guest sets up before the handshake. The guest's ciphertext from `receive` feeds a node:tls
// socket through an in-memory transport, the socket's ciphertext is `send`'s returned stream, and the socket's
// cleartext is `receive`'s. Node.js validates the server's certificate against its roots (NODE_EXTRA_CA_CERTS adds
// to them), for the server name `connect` is given.
// - connect: node:tls's connect, or one that adds options such as a test CA.
export function createNodeWasiTls(services) {
  if (services === null || typeof services !== "object" || typeof services.connect !== "function") {
    throw new TypeError("Node.js wasi:tls requires node:tls's connect");
  }
  const { connect } = services;
  const errors = new WeakMap();
  const connectors = new WeakMap();

  class TlsError {
    constructor() {
      throw new TypeError("a wasi:tls error is created by the host");
    }

    toDebugString() {
      return state(errors, this, "error");
    }
  }

  const failure = message => {
    const error = Object.create(TlsError.prototype);
    errors.set(error, String(message));
    return Object.freeze({ tag: "err", val: error });
  };

  class Connector {
    constructor() {
      const handshake = deferred();
      // A failed handshake is reported by connect; the streams waiting on it just end.
      handshake.promise.catch(ignore);
      connectors.set(this, {
        transport: createTransport(),
        handshake,
        sending: false,
        receiving: false,
        connected: false,
      });
    }

    send(cleartext) {
      const connector = state(connectors, this, "connector");
      if (connector.sending) return refuse(cleartext, failure("send already configured"));
      connector.sending = true;
      const { transport, handshake } = connector;
      const sent = settleWith(failure, (async () => {
        const socket = await handshake.promise;
        const read = createGuestByteReader(cleartext);
        for (;;) {
          const chunk = await read();
          // The end of the cleartext is close_notify, then the end of the ciphertext.
          if (chunk === null) return end(socket);
          await written(socket, chunk);
        }
      })());
      return [transport.ciphertext(), sent];
    }

    receive(ciphertext) {
      const connector = state(connectors, this, "connector");
      if (connector.receiving) return refuse(ciphertext, failure("receive already configured"));
      connector.receiving = true;
      const { transport, handshake } = connector;
      const fed = transport.feed(createGuestByteReader(ciphertext));
      const cleartext = deferred();
      // A failed handshake fails receive's future whether or not the guest reads the cleartext.
      handshake.promise.catch(error => cleartext.reject(error));
      const received = settleWith(failure, Promise.all([fed, cleartext.promise]));
      async function* decrypted() {
        let socket;
        try {
          socket = await handshake.promise;
        } catch {
          return;
        }
        try {
          for await (const chunk of socket) yield new Uint8Array(chunk);
          cleartext.resolve();
        } catch (error) {
          cleartext.reject(error);
          throw error;
        }
      }
      return [decrypted(), received];
    }

    static async connect(connector, serverName) {
      const current = state(connectors, connector, "connector");
      connectors.delete(connector);
      if (!current.sending) return failure("send() must be called before connect()");
      if (!current.receiving) return failure("receive() must be called before connect()");
      if (typeof serverName !== "string") throw new TypeError("the server name must be a string");
      const options = { socket: current.transport.duplex };
      // RFC 6066 names no IP address; Node.js then checks the certificate against the host.
      if (isIP(serverName) === 0) options.servername = serverName;
      else options.host = serverName;
      try {
        const socket = await new Promise((resolve, reject) => {
          const opened = connect(options);
          opened.once("secureConnect", () => resolve(opened));
          opened.once("error", reject);
        });
        current.handshake.resolve(socket);
        return Object.freeze({ tag: "ok", val: undefined });
      } catch (error) {
        current.handshake.reject(error);
        current.transport.duplex.destroy();
        return failure(error.message);
      }
    }
  }

  return Object.freeze({
    "wasi:tls/types@0.3.0-draft": Object.freeze({ Error: TlsError }),
    "wasi:tls/client@0.3.0-draft": Object.freeze({ Connector }),
  });
}

const ignore = () => {};

function state(map, value, label) {
  const found = map.get(value);
  if (found === undefined) throw new TypeError(`wasi:tls ${label} resource is invalid`);
  return found;
}

function deferred() {
  let resolve;
  let reject;
  const promise = new Promise((settle, fail) => {
    resolve = settle;
    reject = fail;
  });
  return { promise, resolve, reject };
}

// The future of a stream transform: ok once its work is done, or the error that stopped it.
function settleWith(failure, work) {
  return work.then(
    () => Object.freeze({ tag: "ok", val: undefined }),
    error => failure(error.message));
}

// A transform configured twice: the guest's stream is read to its end and dropped, and the result is the error.
function refuse(stream, result) {
  const read = createGuestByteReader(stream);
  (async () => { while (await read() !== null); })().catch(ignore);
  return [(async function* () {})(), Promise.resolve(result)];
}

function end(socket) {
  return new Promise((resolve, reject) => {
    socket.once("error", reject);
    socket.end(resolve);
  });
}

// A write completes once node:tls has handed the chunk to the transport, so the guest waits for the network.
function written(socket, chunk) {
  return new Promise((resolve, reject) => {
    socket.write(chunk, error => (error ? reject(error) : resolve()));
  });
}

// The node:tls socket's transport: what it writes the guest takes from `ciphertext()`, a chunk at a time (a write
// completes once taken), and what `feed` reads from the guest it reads.
function createTransport() {
  const pending = [];
  let taker = null;
  let ended = false;
  let wantsMore = null;
  const offer = item => {
    if (taker !== null) {
      const take = taker;
      taker = null;
      take(item);
    } else {
      pending.push(item);
    }
  };
  const duplex = new Duplex({
    read() {
      const resume = wantsMore;
      wantsMore = null;
      resume?.();
    },
    write(chunk, encoding, callback) {
      offer({ chunk: new Uint8Array(chunk), taken: callback });
    },
    final(callback) {
      ended = true;
      offer(null);
      callback();
    },
    destroy(error, callback) {
      if (!ended) {
        ended = true;
        offer(null);
      }
      callback(error);
    },
  });
  return {
    duplex,
    async* ciphertext() {
      for (;;) {
        const item = pending.length !== 0 ? pending.shift()
          : await new Promise(resolve => { taker = resolve; });
        if (item === null) return;
        item.taken();
        yield item.chunk;
      }
    },
    async feed(read) {
      for (;;) {
        const chunk = await read();
        if (chunk === null) {
          duplex.push(null);
          return;
        }
        if (duplex.destroyed) return;
        if (!duplex.push(chunk)) await new Promise(resolve => { wantsMore = resolve; });
      }
    },
  };
}
