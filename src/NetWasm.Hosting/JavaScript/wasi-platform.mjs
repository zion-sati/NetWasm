import { readInputSource, validateInputSource } from "./input-source.mjs";
import { validateOutputSink, writeStreamToSink } from "./output-sink-stream.mjs";

// The WASI 0.3 interfaces NetWasm's JavaScript hosts supply to one execution. The command
// line, environment and standard streams are the execution request's; clocks and random
// numbers come from the JavaScript platform; files come from the execution's filesystem
// scope; HTTP and sockets come from the host's network services, when it has them. A local
// host reports the directory the program starts in; a browser has none.

const requestKeys = ["arguments", "environment", "filesystem", "grants", "signal", "stderr", "stdin", "stdout"];
const serviceKeys = ["initialDirectory", "network"];
const networkKeys = ["modules"];
const maximumTimerDelay = 0x7fffffff;
const randomChunk = 65536;
const nanosecondsPerMillisecond = 1_000_000;
const exitBrand = Symbol.for("netwasm.wasi.exit");

// A guest's wasi:cli/exit call: the host unwinds the guest and completes the execution
// with the status code.
export function isWasiExit(value) {
  return value !== null && typeof value === "object" && value[exitBrand] === true;
}

export function createWasiPlatform(services) {
  assertExactDataObject(services, serviceKeys, "WASI platform services");
  const network = readNetwork(services.network);
  const initialDirectory = readInitialDirectory(services.initialDirectory);
  return Object.freeze(function createWasiImports(request) {
    const { argumentList, environment, filesystem, stdin } = readRequest(request);
    const timers = new Set();
    const drains = new Set();
    let released = false;

    // A guest stream still open at release belongs to a guest that will not write again
    // (it returned or trapped); dropping it ends the host's pending read of it.
    const openStreams = new Set();
    const drain = (stream, sink) => {
      openStreams.add(stream);
      const promise = writeStreamToSink(stream, sink);
      drains.add(promise);
      promise.finally(() => {
        drains.delete(promise);
        openStreams.delete(stream);
      });
      return promise;
    };
    const wait = milliseconds => new Promise(resolve => {
      if (released) return;
      const step = remaining => {
        const delay = Math.min(Math.max(remaining, 0), maximumTimerDelay);
        const timer = setTimeout(() => {
          timers.delete(timer);
          if (remaining > delay) step(remaining - delay);
          else resolve();
        }, delay);
        timers.add(timer);
      };
      step(milliseconds);
    });

    // Every read-via-stream reads the same standard input, opened on the guest's first read.
    let input = null;
    const readStandardInput = () => {
      let settle;
      const future = new Promise(resolve => { settle = resolve; });
      if (stdin === null) {
        settle(Object.freeze({ tag: "ok", val: undefined }));
        return [[], future];
      }
      input ??= stdin.read();
      return [readInputSource(input, settle, () => released), future];
    };

    const terminal = Object.freeze({ getTerminal: () => undefined });
    const modules = {
      "wasi:cli/environment@0.3.2": Object.freeze({
        getEnvironment: () => environment.map(variable => [variable.name, variable.value]),
        getArguments: () => [...argumentList],
        getInitialCwd: () => initialDirectory ?? undefined,
      }),
      "wasi:cli/exit@0.3.2": Object.freeze({
        exit(status) {
          throw createExit(status?.tag === "err" ? 1 : 0);
        },
        exitWithCode(code) {
          throw createExit(code);
        },
      }),
      "wasi:cli/stdin@0.3.2": Object.freeze({
        readViaStream: readStandardInput,
      }),
      "wasi:cli/stdout@0.3.2": Object.freeze({
        writeViaStream: data => drain(data, request.stdout),
      }),
      "wasi:cli/stderr@0.3.2": Object.freeze({
        writeViaStream: data => drain(data, request.stderr),
      }),
      // Type namespaces: their types need no host values.
      "wasi:cli/types@0.3.2": Object.freeze({}),
      "wasi:clocks/types@0.3.2": Object.freeze({}),
      "wasi:cli/terminal-input@0.3.2": Object.freeze({ TerminalInput: class TerminalInput {} }),
      "wasi:cli/terminal-output@0.3.2": Object.freeze({ TerminalOutput: class TerminalOutput {} }),
      "wasi:cli/terminal-stdin@0.3.2": Object.freeze({ getTerminalStdin: terminal.getTerminal }),
      "wasi:cli/terminal-stdout@0.3.2": Object.freeze({ getTerminalStdout: terminal.getTerminal }),
      "wasi:cli/terminal-stderr@0.3.2": Object.freeze({ getTerminalStderr: terminal.getTerminal }),
      "wasi:clocks/monotonic-clock@0.3.2": Object.freeze({
        now: monotonicNow,
        getResolution: () => 1000n,
        waitUntil: when => wait(millisecondsUntil(BigInt.asUintN(64, when) - monotonicNow())),
        waitFor: howLong => wait(millisecondsUntil(BigInt.asUintN(64, howLong))),
      }),
      "wasi:clocks/system-clock@0.3.2": Object.freeze({
        now: systemNow,
        getResolution: () => 1000n,
      }),
      "wasi:random/random@0.3.2": Object.freeze({
        getRandomBytes: randomBytes,
        getRandomU64: randomU64,
      }),
      "wasi:random/insecure@0.3.2": Object.freeze({
        getInsecureRandomBytes: randomBytes,
        getInsecureRandomU64: randomU64,
      }),
      "wasi:random/insecure-seed@0.3.2": Object.freeze({
        getInsecureSeed: () => [randomU64(), randomU64()],
      }),
    };
    if (filesystem !== null) {
      modules["wasi:filesystem/types@0.3.2"] = filesystem.types;
      modules["wasi:filesystem/preopens@0.3.2"] = filesystem.preopens;
    }
    if (network !== null && request.grants.network === "allowAll") {
      Object.assign(modules, network);
    }

    return Object.freeze({
      imports: Object.freeze(modules),
      // After the guest's last export settles: output the guest already wrote reaches its
      // sink, and nothing the guest left waiting keeps the host running.
      async release() {
        if (released) return;
        released = true;
        await Promise.race([Promise.allSettled([...drains]), nextTurn()]);
        for (const stream of openStreams) dropStream(stream);
        openStreams.clear();
        for (const timer of timers) clearTimeout(timer);
        timers.clear();
        // A read the guest left waiting no longer holds the host's input open.
        if (input !== null) dropStream(input);
      },
    });
  });
}

function dropStream(stream) {
  const dispose = stream[Symbol.dispose] ?? stream[Symbol.for("dispose")];
  try {
    if (typeof dispose === "function") dispose.call(stream);
    else if (typeof stream.return === "function") stream.return();
  } catch {
    // The stream's guest already went away; nothing is left to release.
  }
}

function createExit(code) {
  if (!Number.isInteger(code) || code < 0 || code > 255) {
    throw new TypeError("WASI exit status code must be an unsigned 8-bit integer");
  }
  return Object.freeze({ [exitBrand]: true, code });
}

function monotonicNow() {
  return BigInt(Math.floor(performance.now() * nanosecondsPerMillisecond));
}

function systemNow() {
  const milliseconds = performance.timeOrigin + performance.now();
  const seconds = Math.floor(milliseconds / 1000);
  return Object.freeze({
    seconds: BigInt(seconds),
    nanoseconds: Math.min(999_999_999,
      Math.floor((milliseconds - seconds * 1000) * nanosecondsPerMillisecond)),
  });
}

function millisecondsUntil(nanoseconds) {
  if (nanoseconds <= 0n || nanoseconds > 0x7fffffffffffffffn) return 0;
  return Math.ceil(Number(nanoseconds) / nanosecondsPerMillisecond);
}

function randomBytes(maximumLength) {
  const length = Number(BigInt.asUintN(64, maximumLength));
  if (!Number.isSafeInteger(length) || length > 0x7fffffff) {
    throw new RangeError("random byte request is too large");
  }
  const bytes = new Uint8Array(length);
  for (let offset = 0; offset < length; offset += randomChunk) {
    crypto.getRandomValues(bytes.subarray(offset, Math.min(length, offset + randomChunk)));
  }
  return bytes;
}

function randomU64() {
  return crypto.getRandomValues(new BigUint64Array(1))[0];
}

function nextTurn() {
  return new Promise(resolve => setTimeout(resolve, 0));
}

function readNetwork(value) {
  if (value === null) return null;
  assertExactDataObject(value, networkKeys, "WASI network services");
  const modules = value.modules;
  if (modules === null || typeof modules !== "object" || Array.isArray(modules)
      || !Object.isFrozen(modules) || Object.keys(modules).length === 0
      || Object.entries(modules).some(([name, source]) => !/^wasi:(http|sockets)\//u.test(name)
        || source === null || typeof source !== "object")) {
    throw new TypeError("WASI network services must supply frozen wasi:http or wasi:sockets modules");
  }
  return modules;
}

function readRequest(request) {
  assertExactDataObject(request, requestKeys, "WASI platform request");
  if (!Array.isArray(request.arguments) || request.arguments.some(value => typeof value !== "string")
      || !Array.isArray(request.environment)
      || request.environment.some(variable => variable === null || typeof variable !== "object"
        || typeof variable.name !== "string" || typeof variable.value !== "string")
      || request.grants === null || typeof request.grants !== "object") {
    throw new TypeError("WASI platform request is incomplete");
  }
  validateOutputSink(request.stdout, "WASI stdout output sink");
  validateOutputSink(request.stderr, "WASI stderr output sink");
  const stdin = validateInputSource(request.stdin, "WASI stdin input source");
  const filesystem = request.filesystem;
  if (filesystem !== null && (typeof filesystem !== "object"
      || filesystem.types === null || typeof filesystem.types !== "object"
      || filesystem.preopens === null || typeof filesystem.preopens !== "object")) {
    throw new TypeError("WASI filesystem scope must supply types and preopens");
  }
  return { argumentList: request.arguments, environment: request.environment, filesystem, stdin };
}

function assertExactDataObject(value, keys, label) {
  if (value === null || typeof value !== "object" || Array.isArray(value)
      || Object.getOwnPropertySymbols(value).length !== 0) {
    throw new TypeError(`${label} is invalid`);
  }
  const prototype = Object.getPrototypeOf(value);
  const descriptors = Object.getOwnPropertyDescriptors(value);
  const actualKeys = Object.keys(descriptors).sort();
  if (prototype !== null && prototype !== Object.prototype
      || actualKeys.length !== keys.length
      || actualKeys.some((key, index) => key !== keys[index])
      || Object.values(descriptors).some(
        descriptor => !descriptor.enumerable || !("value" in descriptor))) {
    throw new TypeError(`${label} shape is invalid`);
  }
}

function readInitialDirectory(value) {
  if (value !== null && (typeof value !== "string" || !value.startsWith("/") || value.includes("\0"))) {
    throw new TypeError("WASI initial directory must be null or an absolute guest path");
  }
  return value;
}
